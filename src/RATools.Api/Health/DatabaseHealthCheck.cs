using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Api.Health;

/// <summary>
/// 就绪探针的数据库检查：通过 RAToolsDbContext.CanConnectAsync 实际探测 PostgreSQL，
/// 使 /health/ready 真实反映数据库可用性。仅在关系型 provider 下注册；
/// InMemory provider 不注册此检查，/health/ready 因无依赖检查而恒为健康。
/// </summary>
public sealed class DatabaseHealthCheck(RAToolsDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection is available.")
                : HealthCheckResult.Unhealthy("Database connection could not be established.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database connection check failed.", exception);
        }
    }
}
