using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

public sealed partial class PackageValidationBackgroundService(IServiceScopeFactory scopeFactory,
    IOptions<PackageValidationOptions> options, ILogger<PackageValidationBackgroundService> logger) : BackgroundService
{
    private readonly string _owner = $"validation:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(30);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Error, Message = "Independent validation worker failed; persisted leases remain recoverable.")]
    private static partial void LogWorkerFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "PackageValidation:Enabled is false; queued runs will remain Pending until a worker is enabled.")]
    private static partial void LogWorkerDisabled(ILogger logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogWorkerDisabled(logger);
            return;
        }
        var lastRecovery = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PackageValidationLease? lease;
                using (var scope = scopeFactory.CreateScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IPackageValidationStore>();
                    // Expiry recovery only matters on a coarse cadence; scanning every
                    // poll interval would issue tens of thousands of useless updates a day.
                    if (DateTime.UtcNow - lastRecovery >= RecoveryInterval)
                    {
                        await store.RecoverExpiredAsync(DateTime.UtcNow, options.Value.MaxAttempts, stoppingToken).ConfigureAwait(false);
                        lastRecovery = DateTime.UtcNow;
                    }
                    lease = await store.ClaimAsync(_owner, DateTime.UtcNow, options.Value.LeaseDuration, stoppingToken).ConfigureAwait(false);
                }
                if (lease is not null) await ProcessAsync(lease, stoppingToken).ConfigureAwait(false);
                else await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                LogWorkerFailure(logger, exception);
                await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ProcessAsync(PackageValidationLease lease, CancellationToken stoppingToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var monitorStop = new CancellationTokenSource();
        var deadline = DateTime.UtcNow + options.Value.ExecutionTimeout;
        var monitor = MonitorAsync(lease, deadline, execution, monitorStop.Token);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<PackageValidationService>();
            // Keep parser CPU work off the hosted-service startup thread. Inputs are owned
            // until this task finishes, even if the monitor has already recorded a timeout.
            // The projection derived from the same inspection facts is archived with the
            // report; if projection construction fails, the run fails rather than storing
            // a report whose cumulative view silently differs.
            DossierProjection? projection = null;
            var report = await Task.Run(() => service.InspectAsync(lease.Run, async stage =>
            {
                using var progressScope = scopeFactory.CreateScope();
                if (!await progressScope.ServiceProvider.GetRequiredService<IPackageValidationStore>().HeartbeatAsync(
                    lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow, options.Value.LeaseDuration, stage, execution.Token).ConfigureAwait(false))
                { execution.Cancel(); execution.Token.ThrowIfCancellationRequested(); }
            }, value => { projection = value; return Task.CompletedTask; }, execution.Token), execution.Token).ConfigureAwait(false);
            if (DateTime.UtcNow >= deadline)
            {
                await RecordFailureAsync(lease, ValidationRunStatus.Failed, "VALIDATION_TIMEOUT", "Validation exceeded its execution deadline.").ConfigureAwait(false);
                return;
            }
            using var archiveScope = scopeFactory.CreateScope();
            var store = archiveScope.ServiceProvider.GetRequiredService<IPackageValidationStore>();
            if (!await store.HeartbeatAsync(lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
                options.Value.LeaseDuration, "Archiving", execution.Token).ConfigureAwait(false)) return;
            if (!await store.CompleteAsync(lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow, report, projection, execution.Token).ConfigureAwait(false))
                await RecordFailureAsync(lease, ValidationRunStatus.Cancelled, "CANCELLED", "Cancellation prevented report publication.").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Host shutdown leaves the leased run recoverable on restart.
            if (exception is OperationCanceledException && stoppingToken.IsCancellationRequested) return;
            var (status, code) = exception switch
            {
                PackageInputChangedException => (ValidationRunStatus.InputChanged, "INPUT_CHANGED"),
                PackageValidationVersionChangedException => (ValidationRunStatus.Failed, "VALIDATION_VERSION_CHANGED"),
                OperationCanceledException => (ValidationRunStatus.Failed, "WORKER_INTERRUPTED"),
                PackageInputException input => (ValidationRunStatus.Failed, input.Code),
                _ => (ValidationRunStatus.Failed, "VALIDATION_EXECUTION_FAILED")
            };
            await RecordFailureAsync(lease, status, code, exception.Message).ConfigureAwait(false);
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitor.ConfigureAwait(false); }
            catch (OperationCanceledException) when (monitorStop.IsCancellationRequested) { }
        }
    }

    private async Task MonitorAsync(PackageValidationLease lease, DateTime deadline, CancellationTokenSource execution, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.Value.HeartbeatInterval, stop).ConfigureAwait(false);
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IPackageValidationStore>();
                var run = await store.GetRunAsync(lease.Run.Id, stop).ConfigureAwait(false);
                if (run is null || !run.OwnsLease(lease.Token, lease.Owner, DateTime.UtcNow)) { execution.Cancel(); return; }
                if (run.CancellationRequestedUtc is not null || DateTime.UtcNow >= deadline)
                {
                    // A terminal result becomes queryable even if the in-process parser
                    // delays cooperative cancellation. Do not admit another parser until
                    // the first exits, and never dispose its live captures prematurely.
                    await RecordFailureAsync(lease, run.CancellationRequestedUtc is null ? ValidationRunStatus.Failed : ValidationRunStatus.Cancelled,
                        run.CancellationRequestedUtc is null ? "VALIDATION_TIMEOUT" : "CANCELLED",
                        run.CancellationRequestedUtc is null ? "Validation exceeded its execution deadline." : "Cancellation acknowledged.").ConfigureAwait(false);
                    execution.Cancel();
                    return;
                }
                if (!await store.HeartbeatAsync(lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
                    options.Value.LeaseDuration, run.Stage, stop).ConfigureAwait(false)) { execution.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            execution.Cancel();
            LogWorkerFailure(logger, exception);
            // Do not renew a lease when persisted cancellation/state cannot be read.
        }
    }

    private async Task RecordFailureAsync(PackageValidationLease lease, ValidationRunStatus status, string code, string message)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPackageValidationStore>().FailAsync(lease.Run.Id, lease.Token,
            lease.Owner, DateTime.UtcNow, status, code, message, cleanup.Token).ConfigureAwait(false);
    }
}
