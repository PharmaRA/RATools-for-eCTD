using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RATools.Application.Publishing;

namespace RATools.Infrastructure.Publishing;

/// <summary>
/// 基于 System.Threading.Channels 的进程内发布作业队列。容量由配置限制，
/// 满载时由 WriteAsync 等待消费者释放槽位，形成明确的背压。
/// </summary>
public sealed class ChannelPublishJobQueue : IPublishJobQueue
{
    private readonly Channel<QueuedPublishJob> _channel;

    public ChannelPublishJobQueue(IOptions<PublishJobExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capacity = options.Value.QueueCapacity;
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Publish job queue capacity must be greater than zero.");
        }

        _channel = Channel.CreateBounded<QueuedPublishJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public async ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        await _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public ValueTask<QueuedPublishJob> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
