using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 兼容性启动回收：Pending 行已经是持久队列事实，必须保留给 worker 继续认领；
/// 当前仅把上一进程中断的 Running 行标记为 Failed。Phase 3 的下一项会改为只处理
/// 租约已过期的 Running 行，从而解除这里仍然存在的单实例启动假设。
/// </summary>
public sealed partial class StalePublishJobRecoveryService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StalePublishJobRecoveryService> logger) : IHostedService
{
    private const string RecoveryFailureReason =
        "Recovered at startup: the process restarted while this publish job was executing.";

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Failed to write the startup-recovery audit entry for publish job {JobId}; the job itself was recovered.")]
    private static partial void LogRecoveryAuditWriteFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Recovered {Count} publish job(s) left in Running state by a previous process.")]
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

        var staleJobs = (await repository.ListActiveAsync(cancellationToken))
            .Where(job => job.Status == PublishJobStatus.Running)
            .ToArray();
        if (staleJobs.Length == 0)
        {
            return;
        }

        foreach (var job in staleJobs)
        {
            job.MarkFailed(RecoveryFailureReason);
            await repository.UpdateAsync(job, cancellationToken);

            try
            {
                await auditLogService.CreateAsync(
                    new CreateAuditLogRequest(
                        EntityType: "PublishJob",
                        EntityId: job.Id.ToString(),
                        Action: "RecoveredAtStartup",
                        Actor: "system",
                        Details: $"Stale publish job for application {job.ApplicationId}, sequence {job.SequenceNumber} was marked Failed during startup recovery."),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRecoveryAuditWriteFailed(logger, exception, job.Id);
            }
        }

        LogStaleJobsRecovered(logger, staleJobs.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
