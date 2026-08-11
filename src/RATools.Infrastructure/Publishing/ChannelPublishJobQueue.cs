using System.Threading.Channels;
using RATools.Application.Publishing;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 进程内唤醒信号，不是发布作业的事实来源。容量固定为 1 并合并重复通知；
/// worker 超时醒来后仍会查询数据库，所以进程退出或信号丢失不会丢作业。
/// </summary>
public sealed class ChannelPublishJobQueue : IPublishJobQueue
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        _channel.Writer.TryWrite(true);
        return ValueTask.CompletedTask;
    }

    public async ValueTask WaitForWorkAsync(TimeSpan maximumDelay, CancellationToken cancellationToken)
    {
        if (maximumDelay <= TimeSpan.Zero)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(maximumDelay);
        try
        {
            await _channel.Reader.ReadAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Poll interval elapsed; the worker will query the durable queue again.
        }
    }
}
