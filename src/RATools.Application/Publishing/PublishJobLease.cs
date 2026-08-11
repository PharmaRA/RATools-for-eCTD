using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed record PublishJobLease(
    PublishJob Job,
    Guid Token,
    string Owner,
    DateTime ExpiresUtc);

public sealed record PublishJobEnqueueResult(PublishJob Job, bool Created);

public enum PublishJobRetryDisposition
{
    RetryScheduled,
    Failed,
    LeaseLost
}

public sealed record PublishJobRetryResult(
    PublishJobRetryDisposition Disposition,
    PublishJob? Job);
