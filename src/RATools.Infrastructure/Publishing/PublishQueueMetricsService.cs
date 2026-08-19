using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;

namespace RATools.Infrastructure.Publishing;

public sealed partial class PublishQueueMetricsService(
    IServiceScopeFactory scopeFactory,
    IPublishJobMetrics metrics,
    IOptions<MonitoringOptions> monitoringOptions,
    ILogger<PublishQueueMetricsService> logger) : BackgroundService
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Unable to sample the durable publish queue depth.")]
    private static partial void LogQueueSampleFailed(ILogger logger, Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(monitoringOptions.Value.QueueSampleInterval);
        do
        {
            await SampleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPublishJobRepository>();
            metrics.SetQueueDepth(await repository.CountPendingAsync(cancellationToken));
            metrics.SetQueueSampleHealth(successful: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            metrics.SetQueueSampleHealth(successful: false);
            LogQueueSampleFailed(logger, exception);
        }
    }
}
