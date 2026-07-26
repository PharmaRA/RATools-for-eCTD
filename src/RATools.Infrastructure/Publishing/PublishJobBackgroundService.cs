using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Application.Publishing;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 后台宿主服务：从 IPublishJobQueue 取出已入队的发布作业，在独立 DI scope 内
/// 运行发布流程，避免请求结束后 scoped DbContext 被释放。单个作业设有执行超时，
/// 超时则由 ExecuteQueuedAsync 内的状态机将作业标记为 Failed。
/// </summary>
public sealed partial class PublishJobBackgroundService(
    IPublishJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PublishJobBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(15);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error,
        Message = "Background publish job {JobId} failed.")]
    private static partial void LogBackgroundJobFailed(ILogger logger, Exception exception, Guid jobId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            QueuedPublishJob queued;
            try
            {
                queued = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessAsync(queued, stoppingToken);
        }
    }

    private async Task ProcessAsync(QueuedPublishJob queued, CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(ExecutionTimeout);

        using var scope = scopeFactory.CreateScope();
        var publishJobService = scope.ServiceProvider.GetRequiredService<IPublishJobService>();

        try
        {
            await publishJobService.ExecuteQueuedAsync(queued.JobId, queued.Request, timeoutCts.Token);
        }
        catch (Exception exception)
        {
            // ExecuteQueuedAsync 的内部 catch 已将作业标记为 Failed 并写审计；
            // 这里仅记录，避免后台循环因单个作业异常而中断。
            LogBackgroundJobFailed(logger, exception, queued.JobId);
        }
    }
}
