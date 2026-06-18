using System.Threading.Channels;
using RATools.Application.Publishing;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 基于 System.Threading.Channels 的进程内发布作业队列。无界容量，
/// 由后台宿主服务单消费者顺序取出执行。
/// </summary>
public sealed class ChannelPublishJobQueue : IPublishJobQueue
{
    private readonly Channel<QueuedPublishJob> _channel =
        Channel.CreateUnbounded<QueuedPublishJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public async ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        await _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public ValueTask<QueuedPublishJob> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
