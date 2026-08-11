using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 启动时原子回收租约已过期（或迁移前没有租约）的 Running 作业。Pending 行是持久
/// 队列事实，仍由 worker 认领；未过期的租约可能属于另一个实例，绝不能触碰。
/// </summary>
public sealed partial class StalePublishJobRecoveryService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StalePublishJobRecoveryService> logger) : IHostedService
{
    private const string RecoveryFailureReason =
        "Recovered at startup after the publish job execution lease expired or was missing.";

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Failed to write the startup-recovery audit entry for publish job {JobId}; the job itself was recovered.")]
    private static partial void LogRecoveryAuditWriteFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Recovered {Count} publish job(s) with expired or missing execution leases.")]
    private static partial void LogStaleJobsRecovered(ILogger logger, int count);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 用运行时配置判断 provider：注册阶段读不到测试宿主 ConfigureAppConfiguration
        // 的覆盖值，而注入的 IConfiguration 是构建完成的最终配置。InMemory 存储随进程
        // 消亡，重启后不存在遗留行，且测试宿主通常没有可连接的数据库——直接跳过。
        var provider = configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPublishJobRepository>();
        var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var staleJobs = await repository.RecoverExpiredLeasesAsync(
            DateTime.UtcNow,
            RecoveryFailureReason,
            cancellationToken);
        if (staleJobs.Count == 0)
        {
            return;
        }

        foreach (var job in staleJobs)
        {
            try
            {
                await auditLogService.WriteSystemEventAsync(
                    new CreateAuditLogRequest(
                        EntityType: "PublishJob",
                        EntityId: job.Id.ToString(),
                        Action: "RecoveredAtStartup",
                        Details: $"Publish job for application {job.ApplicationId}, sequence {job.SequenceNumber} had an expired or missing lease and was marked Failed during startup recovery."),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRecoveryAuditWriteFailed(logger, exception, job.Id);
            }
        }

        LogStaleJobsRecovered(logger, staleJobs.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
