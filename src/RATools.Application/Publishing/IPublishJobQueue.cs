using RATools.Application.Publishing.Requests;

namespace RATools.Application.Publishing;

/// <summary>
/// 已入队待后台执行的发布作业：作业 id 加上原始创建请求，
/// 后台宿主服务据此在独立 DI scope 内运行发布流程。
/// </summary>
public sealed record QueuedPublishJob(Guid JobId, CreatePublishJobRequest Request);

/// <summary>
/// 发布作业的进程内队列，把发布执行从 HTTP 请求线程解耦到后台宿主服务。
/// </summary>
public interface IPublishJobQueue
{
    ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default);

    ValueTask<QueuedPublishJob> DequeueAsync(CancellationToken cancellationToken);
}
