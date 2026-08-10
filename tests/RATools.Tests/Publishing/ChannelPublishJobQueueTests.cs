using Microsoft.Extensions.Options;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class ChannelPublishJobQueueTests
{
    [Fact]
    public async Task EnqueueAsync_WaitsAndCanBeCanceledWhenCapacityIsFull()
    {
        var queue = CreateQueue(capacity: 1);
        var first = Job("0001");
        var second = Job("0002");
        await queue.EnqueueAsync(first);
        using var enqueueCts = new CancellationTokenSource();

        var blockedEnqueue = queue.EnqueueAsync(second, enqueueCts.Token).AsTask();
        await Task.Delay(25);

        Assert.False(blockedEnqueue.IsCompleted);
        enqueueCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockedEnqueue);
        Assert.Equal(first, await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnqueueAsync_ContinuesAfterConsumerReleasesCapacity()
    {
        var queue = CreateQueue(capacity: 1);
        var first = Job("0001");
        var second = Job("0002");
        await queue.EnqueueAsync(first);

        var blockedEnqueue = queue.EnqueueAsync(second).AsTask();
        Assert.Equal(first, await queue.DequeueAsync(CancellationToken.None));
        await blockedEnqueue.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(second, await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        var options = Options.Create(new PublishJobExecutionOptions { QueueCapacity = 0 });

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelPublishJobQueue(options));

        Assert.Contains("capacity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ChannelPublishJobQueue CreateQueue(int capacity)
        => new(Options.Create(new PublishJobExecutionOptions { QueueCapacity = capacity }));

    private static QueuedPublishJob Job(string sequenceNumber)
    {
        var request = new CreatePublishJobRequest(Guid.NewGuid(), sequenceNumber);
        return new QueuedPublishJob(Guid.NewGuid(), request);
    }
}
