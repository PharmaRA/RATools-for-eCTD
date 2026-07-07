using RATools.Application.Publishing;

namespace RATools.Tests.Publishing;

/// <summary>
/// 测试用发布作业队列：直接记录入队项，不做后台执行。
/// 单元/集成测试通过直接调用服务方法验证发布流程，无需真实后台宿主。
/// </summary>
public sealed class FakePublishJobQueue : IPublishJobQueue
{
    private readonly List<QueuedPublishJob> _enqueued = [];

    public IReadOnlyList<QueuedPublishJob> Enqueued => _enqueued;

    public ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default)
    {
        _enqueued.Add(job);
        return ValueTask.CompletedTask;
    }

    public ValueTask<QueuedPublishJob> DequeueAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("FakePublishJobQueue does not support dequeue.");
    }
}
