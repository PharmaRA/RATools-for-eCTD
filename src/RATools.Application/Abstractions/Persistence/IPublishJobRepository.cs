using RATools.Application.Publishing;
using RATools.Domain.Publishing;

namespace RATools.Application.Abstractions.Persistence;

public interface IPublishJobRepository
{
    Task AddAsync(PublishJob job, CancellationToken cancellationToken = default);

    async Task<PublishJobEnqueueResult> AddOrGetByIdempotencyKeyAsync(
        PublishJob job,
        CancellationToken cancellationToken = default)
    {
        await AddAsync(job, cancellationToken);
        return new PublishJobEnqueueResult(job, Created: true);
    }

    Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default);

    Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PublishJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => Task.FromResult<PublishJob?>(null);

    Task<PublishJobLease?> TryClaimNextAsync(
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
        => Task.FromResult<PublishJobLease?>(null);

    Task<bool> RenewLeaseAsync(
        Guid jobId,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    Task<bool> UpdateLeasedAsync(
        PublishJob job,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    Task<PublishJobRetryResult> RetryOrFailLeasedAsync(
        Guid jobId,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        DateTime nextAttemptUtc,
        int maxAttempts,
        string failureReason,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PublishJobRetryResult(PublishJobRetryDisposition.LeaseLost, null));

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
