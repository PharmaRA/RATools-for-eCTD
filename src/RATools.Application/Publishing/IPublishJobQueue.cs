using RATools.Application.Publishing.Requests;

namespace RATools.Application.Publishing;

/// <summary>
/// 数据库已持久化的发布作业通知。该消息只用于降低轮询延迟，不承载队列事实；
/// 即使通知丢失，worker 仍会从数据库认领 Pending 作业。
/// </summary>
public sealed record QueuedPublishJob(Guid JobId, CreatePublishJobRequest Request);

/// <summary>
/// 持久发布队列的进程内唤醒信号。
/// </summary>
public interface IPublishJobQueue
{
    ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default);

    ValueTask WaitForWorkAsync(TimeSpan maximumDelay, CancellationToken cancellationToken);
}
