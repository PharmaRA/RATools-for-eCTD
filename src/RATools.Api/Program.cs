using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Reflection;
using RATools.Api.Health;
using RATools.Api.Middleware;
using RATools.Api.Security;
using RATools.Application;
using RATools.Infrastructure;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(ApiKeyAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.ApiKey = builder.Configuration.GetValue<string>("Security:ApiKey") ?? string.Empty;
        });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SecurityPolicyNames.HighRiskFilesystemAccess, policy =>
    {
        policy.AuthenticationSchemes.Add(ApiKeyAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 健康检查：存活探针不含依赖；就绪探针（ready 标签）在关系型 provider 下探测数据库。
var healthChecksBuilder = builder.Services.AddHealthChecks();
var persistenceProvider = builder.Configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
if (!string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    healthChecksBuilder.AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });
}

var app = builder.Build();

var provider = app.Configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RAToolsDbContext>();
    dbContext.Database.Migrate();
}

if (!string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    using var validatorScope = app.Services.CreateScope();
    var validator = validatorScope.ServiceProvider.GetRequiredService<StartupConfigurationValidator>();
    validator.Validate();
}

var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", true);

app.UseMiddleware<GlobalExceptionMiddleware>();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Redirect(swaggerEnabled ? "/swagger" : "/health")).AllowAnonymous();
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
app.Run();

public partial class Program;
