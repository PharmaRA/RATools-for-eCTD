using System.Diagnostics;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class ChannelPublishJobQueueTests
{
    [Fact]
    public async Task WaitForWorkAsync_ReturnsWhenDurableJobNotificationArrives()
    {
        var queue = new ChannelPublishJobQueue();
        var wait = queue.WaitForWorkAsync(TimeSpan.FromSeconds(5), CancellationToken.None).AsTask();

        await queue.EnqueueAsync(Job());

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForWorkAsync_ReturnsAfterPollingDelayWithoutNotification()
    {
        var queue = new ChannelPublishJobQueue();
        var stopwatch = Stopwatch.StartNew();

        await queue.WaitForWorkAsync(TimeSpan.FromMilliseconds(40), CancellationToken.None);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task EnqueueAsync_CoalescesRepeatedWakeSignals()
    {
        var queue = new ChannelPublishJobQueue();
        await queue.EnqueueAsync(Job());
        await queue.EnqueueAsync(Job());

        await queue.WaitForWorkAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        var stopwatch = Stopwatch.StartNew();
        await queue.WaitForWorkAsync(TimeSpan.FromMilliseconds(40), CancellationToken.None);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(20));
    }

    private static QueuedPublishJob Job()
        => new(Guid.NewGuid(), new CreatePublishJobRequest(Guid.NewGuid(), "0001"));
}
