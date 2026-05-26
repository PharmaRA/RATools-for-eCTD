using Microsoft.EntityFrameworkCore;
using System.Reflection;
using RATools.Api.Security;
using RATools.Application;
using RATools.Infrastructure;
using RATools.Infrastructure.Persistence.EfCore;

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
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var provider = app.Configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RAToolsDbContext>();
    dbContext.Database.Migrate();
}

var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", true);

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Redirect(swaggerEnabled ? "/swagger" : "/health"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/version", () =>
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    return Results.Ok(new { name = "RATools.Api", version });
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
