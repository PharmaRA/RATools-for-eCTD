using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 启动回收：发布作业先落库（Pending）再进入纯进程内队列，进程重启会丢失队列条目，
/// 数据库中遗留的 Pending/Running 行会永久占用活动作业唯一索引，使对应序列无法再发布。
/// 本服务在宿主启动阶段（先于 PublishJobBackgroundService 消费队列、先于 API 接收请求）
/// 把所有遗留活动作业标记为 Failed 并写审计。此时进程内队列必为空，因此数据库中的
/// 活动作业只可能来自上一个进程，回收不会误伤本进程作业。
/// </summary>
public sealed class StalePublishJobRecoveryService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StalePublishJobRecoveryService> logger) : IHostedService
{
    private const string RecoveryFailureReason =
        "Recovered at startup: the process restarted while this job was queued or executing, so its in-process queue entry was lost.";

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

        var staleJobs = await repository.ListActiveAsync(cancellationToken);
        if (staleJobs.Count == 0)
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
                logger.LogWarning(
                    exception,
                    "Failed to write the startup-recovery audit entry for publish job {JobId}; the job itself was recovered.",
                    job.Id);
            }
        }

        logger.LogWarning(
            "Recovered {Count} stale publish job(s) left in Pending/Running state by a previous process.",
            staleJobs.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
