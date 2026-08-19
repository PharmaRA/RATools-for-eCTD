using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishQueueMetricsServiceTests
{
    [Fact]
    public async Task StartAsync_SamplesDurablePendingJobsImmediately()
    {
        var repository = new InMemoryPublishJobRepository();
        await repository.AddAsync(new PublishJob(Guid.NewGuid(), "0001"));
        var running = new PublishJob(Guid.NewGuid(), "0002");
        running.Claim("worker", DateTime.UtcNow, TimeSpan.FromMinutes(1));
        await repository.AddAsync(running);

        var services = new ServiceCollection();
        services.AddSingleton<IPublishJobRepository>(repository);
        using var provider = services.BuildServiceProvider();
        var metrics = new FakePublishJobMetrics();
        var service = new PublishQueueMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            metrics,
            Options.Create(new MonitoringOptions { QueueSampleInterval = TimeSpan.FromHours(1) }),
            NullLogger<PublishQueueMetricsService>.Instance);

        await service.StartAsync(CancellationToken.None);
        var depth = await metrics.QueueDepthRecorded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, depth);
        Assert.True(metrics.QueueSampleHealthy);
    }
}
