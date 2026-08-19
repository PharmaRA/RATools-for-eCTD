using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 从数据库原子认领发布作业，并在执行期间维持带 fencing token 的租约。
/// Channel 只负责唤醒；数据库中的 Pending 行是可跨重启保留的队列事实。
/// </summary>
public sealed partial class PublishJobBackgroundService(
    IPublishJobQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<PublishJobExecutionOptions> executionOptions,
    IPublishJobMetrics metrics,
    ILogger<PublishJobBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error,
        Message = "Background publish job {JobId} attempt failed.")]
    private static partial void LogBackgroundJobFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Publish job {JobId} lease was lost by worker {Owner}; stale execution cannot persist state.")]
    private static partial void LogLeaseLost(ILogger logger, Guid jobId, string owner);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Publish job {JobId} scheduled for retry after attempt {AttemptCount}.")]
    private static partial void LogRetryScheduled(ILogger logger, Guid jobId, int attemptCount);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error,
        Message = "Publish job {JobId} exhausted {AttemptCount} attempt(s) and was marked Failed.")]
    private static partial void LogRetriesExhausted(ILogger logger, Guid jobId, int attemptCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = executionOptions.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            PublishJobLease? lease;
            using (var scope = scopeFactory.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IPublishJobRepository>();
                lease = await repository.TryClaimNextAsync(
                    _owner,
                    DateTime.UtcNow,
                    options.LeaseDuration,
                    options.MaxAttempts,
                    stoppingToken);
            }

            if (lease is null)
            {
                await queue.WaitForWorkAsync(options.PollInterval, stoppingToken);
                continue;
            }

            await ProcessAsync(lease, options, stoppingToken);
        }
    }

    private async Task ProcessAsync(
        PublishJobLease lease,
        PublishJobExecutionOptions options,
        CancellationToken stoppingToken)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        var attemptOutcome = PublishJobAttemptOutcome.Error;

        try
        {
            attemptOutcome = await ProcessCoreAsync(lease, options, stoppingToken);
        }
        finally
        {
            metrics.RecordAttempt(attemptOutcome, Stopwatch.GetElapsedTime(startedTimestamp));
        }
    }

    private async Task<PublishJobAttemptOutcome> ProcessCoreAsync(
        PublishJobLease lease,
        PublishJobExecutionOptions options,
        CancellationToken stoppingToken)
    {
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        executionCts.CancelAfter(options.ExecutionTimeout);
        using var heartbeatStopCts = new CancellationTokenSource();
        var heartbeatTask = MaintainHeartbeatAsync(lease, options, executionCts, heartbeatStopCts.Token);

        Exception? executionFailure = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var publishJobService = scope.ServiceProvider.GetRequiredService<IPublishJobService>();
            await publishJobService.ExecuteClaimedAsync(lease, executionCts.Token);
        }
        catch (Exception exception)
        {
            executionFailure = exception;
            LogBackgroundJobFailed(logger, exception, lease.Job.Id);
        }
        finally
        {
            heartbeatStopCts.Cancel();
        }

        Exception? heartbeatFailure = null;
        try
        {
            await heartbeatTask;
        }
        catch (Exception exception)
        {
            heartbeatFailure = exception;
        }

        if (executionFailure is null && heartbeatFailure is null)
        {
            if (lease.Job.Status is PublishJobStatus.Completed or PublishJobStatus.Failed)
            {
                metrics.RecordTerminal(lease.Job);
                return lease.Job.Status == PublishJobStatus.Completed
                    ? PublishJobAttemptOutcome.Completed
                    : PublishJobAttemptOutcome.Failed;
            }
            return PublishJobAttemptOutcome.Error;
        }

        if (lease.Job.Status is PublishJobStatus.Completed or PublishJobStatus.Failed)
        {
            metrics.RecordTerminal(lease.Job);
        }

        if (executionFailure is PublishJobLeaseLostException
            || heartbeatFailure is PublishJobLeaseLostException)
        {
            LogLeaseLost(logger, lease.Job.Id, lease.Owner);
            return PublishJobAttemptOutcome.LeaseLost;
        }

        var failure = executionFailure ?? heartbeatFailure!;
        var result = await RetryOrFailAsync(lease, options, failure);
        var outcome = result.Disposition switch
        {
            PublishJobRetryDisposition.RetryScheduled => PublishJobAttemptOutcome.Retry,
            PublishJobRetryDisposition.Failed => PublishJobAttemptOutcome.Failed,
            PublishJobRetryDisposition.LeaseLost => PublishJobAttemptOutcome.LeaseLost,
            _ => PublishJobAttemptOutcome.Error,
        };
        if (result.Disposition == PublishJobRetryDisposition.Failed)
        {
            metrics.RecordTerminal(result.Job!);
        }

        return outcome;
    }

    private async Task MaintainHeartbeatAsync(
        PublishJobLease lease,
        PublishJobExecutionOptions options,
        CancellationTokenSource executionCts,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPublishJobRepository>();
                var renewed = await repository.RenewLeaseAsync(
                    lease.Job.Id,
                    lease.Token,
                    lease.Owner,
                    DateTime.UtcNow,
                    options.LeaseDuration,
                    cancellationToken);
                if (!renewed)
                {
                    var current = await repository.GetAsync(lease.Job.Id, cancellationToken);
                    if (current?.Status is PublishJobStatus.Completed or PublishJobStatus.Failed)
                    {
                        // 业务服务先以 fencing token 落终态，再生成报告；终态已证明本 worker
                        // 成功提交，后续报告 I/O 不应被一次预期的续租失败取消。
                        return;
                    }

                    executionCts.Cancel();
                    throw new PublishJobLeaseLostException(lease.Job.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal completion stops the heartbeat loop.
        }
        catch
        {
            executionCts.Cancel();
            throw;
        }
    }

    private async Task<PublishJobRetryResult> RetryOrFailAsync(
        PublishJobLease lease,
        PublishJobExecutionOptions options,
        Exception failure)
    {
        using var cleanupCts = new CancellationTokenSource(CleanupTimeout);
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPublishJobRepository>();
        var reason = failure is OperationCanceledException
            ? "Publish execution was canceled or timed out; the host may have stopped."
            : failure.Message;
        var nowUtc = DateTime.UtcNow;
        var result = await repository.RetryOrFailLeasedAsync(
            lease.Job.Id,
            lease.Token,
            lease.Owner,
            nowUtc,
            nowUtc.Add(options.RetryDelay),
            options.MaxAttempts,
            reason,
            cleanupCts.Token);

        switch (result.Disposition)
        {
            case PublishJobRetryDisposition.RetryScheduled:
                LogRetryScheduled(logger, lease.Job.Id, result.Job!.AttemptCount);
                await queue.EnqueueAsync(
                    new QueuedPublishJob(
                        lease.Job.Id,
                        new CreatePublishJobRequest(
                            lease.Job.ApplicationId,
                            lease.Job.SequenceNumber,
                            lease.Job.IdempotencyKey)),
                    CancellationToken.None);
                break;
            case PublishJobRetryDisposition.Failed:
                LogRetriesExhausted(logger, lease.Job.Id, result.Job!.AttemptCount);
                break;
            case PublishJobRetryDisposition.LeaseLost:
                LogLeaseLost(logger, lease.Job.Id, lease.Owner);
                break;
        }

        return result;
    }
}
