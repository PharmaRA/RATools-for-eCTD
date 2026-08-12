using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Reflection;
using RATools.Api.Health;
using RATools.Api.Middleware;
using RATools.Api.OpenApi;
using RATools.Api.Security;
using RATools.Application;
using RATools.Infrastructure;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// 非开发环境输出单行 JSON 日志（含 scope），便于日志系统采集与检索；
// 开发环境保留默认控制台格式以便人读。
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
    });
}

// 请求体上限：上传是流式处理，但无显式上限时仅靠 ASP.NET Core 默认值（30MB），
// 且该默认会随宿主/反代配置漂移。eCTD 单文件按 FDA 建议以 500MB 为界，可配置。
var maxUploadBytes = builder.Configuration.GetValue("FileStorage:MaxUploadBytes", 524_288_000L);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(ApiKeyAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.ApiKey = builder.Configuration.GetValue<string>("Security:ApiKey") ?? string.Empty;
        });
// purge（递归删除工作区）的最小防护：此前 HighRiskFilesystemAccess 与 FallbackPolicy
// 完全等价（都只要求认证），持有 API Key 即可删除整个工作区目录。在不引入用户/角色
// 体系的前提下（认证增强已决策暂缓），用显式配置开关把破坏性操作默认关死：
// 生产默认 false，需要 purge 时显式开启 Security:AllowDestructiveOperations。
var allowDestructiveOperations = builder.Configuration.GetValue("Security:AllowDestructiveOperations", false);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SecurityPolicyNames.HighRiskFilesystemAccess, policy =>
    {
        policy.AuthenticationSchemes.Add(ApiKeyAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(_ => allowDestructiveOperations);
    });
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
    options.SchemaFilter<NonNullablePropertiesRequiredSchemaFilter>();
});

// 健康检查：存活探针不含依赖；就绪探针（ready 标签）在关系型 provider 下探测数据库。
var healthChecksBuilder = builder.Services.AddHealthChecks();
var persistenceProvider = builder.Configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
if (!string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    healthChecksBuilder.AddCheck<DatabaseHealthCheck>("database", tags: HealthCheckTags.Ready);
}

var app = builder.Build();

var frontendIndex = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
var frontendAvailable = frontendIndex.Exists;

var provider = app.Configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
using (var validatorScope = app.Services.CreateScope())
{
    validatorScope.ServiceProvider.GetRequiredService<LocalOnlyDeploymentValidator>().Validate();
    if (!string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
    {
        validatorScope.ServiceProvider.GetRequiredService<StartupConfigurationValidator>().Validate();
    }
}

if (!string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    app.Services.GetRequiredService<LocalOnlyInstanceLock>().Acquire();
}

if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RAToolsDbContext>();
    dbContext.Database.Migrate();
}

var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", true);

app.UseMiddleware<GlobalExceptionMiddleware>();

if (frontendAvailable)
{
    app.UseStaticFiles();
}

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", (HttpContext context) => frontendAvailable
    ? CreateFrontendIndexResult(context)
    : Results.Redirect(swaggerEnabled ? "/swagger" : "/health")).AllowAnonymous();

if (frontendAvailable || app.Environment.IsDevelopment())
{
    app.MapGet("/runtime-config", (HttpContext context) =>
    {
        var apiKey = app.Configuration.GetValue<string>("Security:ApiKey") ?? string.Empty;
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { apiKey });
    }).AllowAnonymous().ExcludeFromDescription();
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// 存活探针：进程在跑即 200，不探测依赖，供编排器存活探针使用。
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// 就绪探针：聚合 ready 标签下的检查（数据库等），不就绪时返回 503，供负载均衡器/就绪探针使用。
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapGet("/version", () =>
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    return Results.Ok(new { name = "RATools.Api", version });
}).AllowAnonymous();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (frontendAvailable)
{
    app.MapFallback(async context =>
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || IsReservedServerPath(context.Request.Path)
            || !AcceptsHtml(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await CreateFrontendIndexResult(context).ExecuteAsync(context);
    }).AllowAnonymous();
}
app.Run();

IResult CreateFrontendIndexResult(HttpContext context)
{
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Stream(frontendIndex.CreateReadStream(), "text/html; charset=utf-8");
}

static bool AcceptsHtml(HttpRequest request)
    => request.GetTypedHeaders().Accept?.Any(value =>
        string.Equals(value.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase)) == true;

static bool IsReservedServerPath(PathString path)
    => path.StartsWithSegments("/api")
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/swagger")
        || path.StartsWithSegments("/version");

file static class HealthCheckTags
{
    public static readonly string[] Ready = ["ready"];
}

public partial class Program;
