using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;

namespace RATools.Infrastructure.Assembly;

public sealed partial class AssemblyBackgroundService(IServiceScopeFactory scopes, AssemblyExecutionOptions options,
    ILogger<AssemblyBackgroundService> logger) : BackgroundService
{
    private readonly string _owner = "assembly-" + Guid.NewGuid().ToString("N");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PdfProcessingLease? lease;
                await using (var scope = scopes.CreateAsyncScope())
                    lease = await scope.ServiceProvider.GetRequiredService<IAssemblyStore>().ClaimAsync(_owner, DateTime.UtcNow, options.LeaseDuration, stoppingToken).ConfigureAwait(false);
                if (lease is null) { await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false); continue; }
                await RunAsync(lease, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { WorkerFailure(logger, error.GetType().Name); await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false); }
        }
    }
    private async Task RunAsync(PdfProcessingLease lease, CancellationToken stoppingToken)
    {
        using var deadline = new CancellationTokenSource(options.ExecutionTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
        using var monitorToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var monitor = MonitorAsync(lease, operation, monitorToken.Token);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AssemblyService>().ExecuteAsync(lease, operation.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!stoppingToken.IsCancellationRequested)
            {
                await using var scope = scopes.CreateAsyncScope(); using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var store = scope.ServiceProvider.GetRequiredService<IAssemblyStore>();
                var job = await store.GetJobAsync(lease.Job.Id, cleanup.Token).ConfigureAwait(false);
                var status = job?.State.CancellationRequested == true ? PdfProcessingJobStatus.Cancelled :
                    deadline.IsCancellationRequested || error is TimeoutException ? PdfProcessingJobStatus.TimedOut : PdfProcessingJobStatus.Failed;
                await store.FailAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, status,
                    status == PdfProcessingJobStatus.Cancelled ? "CANCELLED" : status == PdfProcessingJobStatus.TimedOut ? "TIMEOUT" : "ASSEMBLY_FAILED",
                    "Assembly did not commit. Sources and task-owned preparation evidence were retained; create a new preview after resolving the issue.", cleanup.Token).ConfigureAwait(false);
            }
        }
        finally { await monitorToken.CancelAsync().ConfigureAwait(false); try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) when (monitorToken.IsCancellationRequested) { } }
    }
    private async Task MonitorAsync(PdfProcessingLease lease, CancellationTokenSource operation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                await using var scope = scopes.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IAssemblyStore>();
                var job = await store.GetJobAsync(lease.Job.Id, cancellationToken).ConfigureAwait(false);
                if (job?.State.Status == PdfProcessingJobStatus.Completed) return;
                if (job is null || job.State.CancellationRequested || !await store.HeartbeatAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, options.LeaseDuration, cancellationToken).ConfigureAwait(false))
                { await operation.CancelAsync().ConfigureAwait(false); return; }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { await operation.CancelAsync().ConfigureAwait(false); throw; }
    }
    [LoggerMessage(8910, LogLevel.Error, "Assembly worker could not advance ({ExceptionType}).")]
    private static partial void WorkerFailure(ILogger logger, string exceptionType);
}
