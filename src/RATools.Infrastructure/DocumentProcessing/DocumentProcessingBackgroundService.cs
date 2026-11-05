using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Application.DocumentProcessing;

namespace RATools.Infrastructure.DocumentProcessing;

// PDF work has an independent, single-process execution slot. It cannot consume
// a publication worker; expired leases allow recovery after a host restart.
public sealed partial class DocumentProcessingBackgroundService(IServiceScopeFactory scopes,
    PdfProcessingExecutionOptions options, ILogger<DocumentProcessingBackgroundService> logger) : BackgroundService
{
    private readonly string _owner = "pdf-" + Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PdfProcessingLease? lease;
                await using (var scope = scopes.CreateAsyncScope())
                    lease = await scope.ServiceProvider.GetRequiredService<IDocumentProcessingStore>()
                        .ClaimAsync(_owner, DateTime.UtcNow, options.LeaseDuration, stoppingToken).ConfigureAwait(false);
                if (lease is null)
                {
                    await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }
                await RunAsync(lease, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                WorkerFailed(logger, exception.GetType().Name);
                await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAsync(PdfProcessingLease lease, CancellationToken stoppingToken)
    {
        using var deadline = new CancellationTokenSource(options.ExecutionTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
        using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var monitor = MonitorAsync(lease, operation, monitoring.Token);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DocumentProcessingService>().ExecuteAsync(lease, operation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!stoppingToken.IsCancellationRequested)
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IDocumentProcessingStore>();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var current = await store.GetJobAsync(lease.Job.Id, cleanup.Token).ConfigureAwait(false);
                var status = current?.CancellationRequested == true ? PdfProcessingJobStatus.Cancelled :
                    deadline.IsCancellationRequested || exception is TimeoutException ? PdfProcessingJobStatus.TimedOut : PdfProcessingJobStatus.Failed;
                var code = status == PdfProcessingJobStatus.Cancelled ? "CANCELLED" : status == PdfProcessingJobStatus.TimedOut ? "TIMEOUT" : "PROCESSING_FAILED";
                // Never archive process arguments, server paths or arbitrary tool
                // stderr in a client-visible failure. Failed attempt files remain
                // under the task directory for controlled operational inspection.
                var message = status == PdfProcessingJobStatus.Cancelled ? "PDF processing was cancelled." :
                    status == PdfProcessingJobStatus.TimedOut ? "PDF processing exceeded its time limit." :
                    "PDF processing or result verification failed. Source content was retained; review the plan and retry with a new preview.";
                await store.FailAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, status, code, message, cleanup.Token).ConfigureAwait(false);
            }
            // Host shutdown leaves the lease for bounded recovery. An old worker
            // cannot register an output after another worker claims that lease.
        }
        finally
        {
            await monitoring.CancelAsync().ConfigureAwait(false);
            try { await monitor.ConfigureAwait(false); }
            catch (OperationCanceledException) when (monitoring.IsCancellationRequested) { }
        }
    }

    private async Task MonitorAsync(PdfProcessingLease lease, CancellationTokenSource operation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IDocumentProcessingStore>();
                var job = await store.GetJobAsync(lease.Job.Id, cancellationToken).ConfigureAwait(false);
                if (job?.Status == PdfProcessingJobStatus.Completed) return;
                if (job is null || job.CancellationRequested || !await store.HeartbeatAsync(lease.Job.Id, lease.Token, lease.Owner,
                        DateTime.UtcNow, options.LeaseDuration, "processing-and-verifying", cancellationToken).ConfigureAwait(false))
                {
                    await operation.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            await operation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    [LoggerMessage(8901, LogLevel.Error, "PDF processing worker could not advance ({ExceptionType}).")]
    private static partial void WorkerFailed(ILogger logger, string exceptionType);
}
