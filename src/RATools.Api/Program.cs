using Microsoft.EntityFrameworkCore;
using System.Reflection;
using RATools.Application;
using RATools.Infrastructure;
using RATools.Infrastructure.Persistence.EfCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
app.MapControllers();
app.Run();

public partial class Program;
