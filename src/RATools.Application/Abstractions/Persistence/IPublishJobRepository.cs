using RATools.Domain.Publishing;

namespace RATools.Application.Abstractions.Persistence;

public interface IPublishJobRepository
{
    Task AddAsync(PublishJob job, CancellationToken cancellationToken = default);

    Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default);

    Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出全部处于 Pending/Running 的活动作业。用于进程启动时回收上一进程遗留的
    /// 幽灵作业——它们占用活动作业唯一索引，会永久阻塞对应序列再次发布。
    /// </summary>
    Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default);

    Task DeleteByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("DeleteByApplicationAsync is not implemented by this repository."));

    Task DeleteBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("DeleteBySequenceAsync is not implemented by this repository."));
}
