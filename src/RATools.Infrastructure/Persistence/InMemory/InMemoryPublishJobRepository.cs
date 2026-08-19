using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryPublishJobRepository : IPublishJobRepository
{
    private readonly ConcurrentDictionary<Guid, PublishJob> _items = new();
    private readonly ConcurrentDictionary<Guid, PublishJobHistorySummary> _historySummaries = new();
    private readonly object _activeJobGate = new();

    public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        // 与关系型 provider 的活动作业部分唯一索引等价：同一应用/序列同时只允许一个
        // Pending/Running 作业。检查与写入需原子，避免并发下产生重复活动作业。
        lock (_activeJobGate)
        {
            if (IsActiveStatus(job.Status) && HasActiveJob(job.ApplicationId, job.SequenceNumber))
            {
                throw new PublishJobAlreadyInProgressException(
                    $"A publish job is already pending or running for application {job.ApplicationId}, sequence {job.SequenceNumber}.");
            }

            _items[job.Id] = Clone(job);
        }

        return Task.CompletedTask;
    }

    public Task<PublishJobEnqueueResult> AddOrGetByIdempotencyKeyAsync(
        PublishJob job,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            var existing = _items.Values.SingleOrDefault(x => x.IdempotencyKey == job.IdempotencyKey);
            if (existing is not null)
            {
                if (existing.ApplicationId != job.ApplicationId
                    || !string.Equals(existing.SequenceNumber, job.SequenceNumber, StringComparison.Ordinal))
                {
                    throw new PublishJobIdempotencyConflictException(job.IdempotencyKey);
                }

                return Task.FromResult(new PublishJobEnqueueResult(Clone(existing), Created: false));
            }

            if (IsActiveStatus(job.Status) && HasActiveJob(job.ApplicationId, job.SequenceNumber))
            {
                throw new PublishJobAlreadyInProgressException(
                    $"A publish job is already pending or running for application {job.ApplicationId}, sequence {job.SequenceNumber}.");
            }

            _items[job.Id] = Clone(job);
            return Task.FromResult(new PublishJobEnqueueResult(Clone(job), Created: true));
        }
    }

    private bool HasActiveJob(Guid applicationId, string sequenceNumber)
    {
        return _items.Values.Any(x =>
            x.ApplicationId == applicationId
            && x.SequenceNumber == sequenceNumber
            && IsActiveStatus(x.Status));
    }

    private static bool IsActiveStatus(PublishJobStatus status)
        => status is PublishJobStatus.Pending or PublishJobStatus.Running;

    public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            _items[job.Id] = Clone(job);
        }

        return Task.CompletedTask;
    }

    public Task<bool> UpdateHistorySummaryAsync(
        Guid jobId,
        int expectedAttemptCount,
        PublishJobHistorySummary summary,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            if (!_items.TryGetValue(jobId, out var job)
                || job.AttemptCount != expectedAttemptCount
                || job.Status is not (PublishJobStatus.Completed or PublishJobStatus.Failed))
            {
                return Task.FromResult(false);
            }

            _historySummaries[jobId] = summary;
            return Task.FromResult(true);
        }
    }

    public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            _items.TryGetValue(id, out var job);
            return Task.FromResult(job is null ? null : Clone(job));
        }
    }

    public Task<PublishJob?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            var job = _items.Values.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            return Task.FromResult(job is null ? null : Clone(job));
        }
    }

    public Task<PublishJobLease?> TryClaimNextAsync(
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            var job = _items.Values
                .Where(x => x.Status == PublishJobStatus.Pending
                    && x.NextAttemptUtc <= nowUtc
                    && x.AttemptCount < maxAttempts)
                .OrderBy(x => x.NextAttemptUtc)
                .ThenBy(x => x.CreatedUtc)
                .FirstOrDefault();
            if (job is null)
            {
                return Task.FromResult<PublishJobLease?>(null);
            }

            var token = job.Claim(owner, nowUtc, leaseDuration);
            _items[job.Id] = Clone(job);
            return Task.FromResult<PublishJobLease?>(
                new PublishJobLease(Clone(job), token, owner, nowUtc.Add(leaseDuration)));
        }
    }

    public Task<bool> RenewLeaseAsync(
        Guid jobId,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            if (!_items.TryGetValue(jobId, out var job)
                || job.LeaseToken != leaseToken
                || !string.Equals(job.LeaseOwner, owner, StringComparison.Ordinal)
                || job.LeaseExpiresUtc <= nowUtc)
            {
                return Task.FromResult(false);
            }

            job.RenewLease(leaseToken, owner, nowUtc, leaseDuration);
            _items[job.Id] = Clone(job);
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateLeasedAsync(
        PublishJob job,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            if (!_items.TryGetValue(job.Id, out var stored)
                || stored.Status != PublishJobStatus.Running
                || stored.LeaseToken != leaseToken
                || !string.Equals(stored.LeaseOwner, owner, StringComparison.Ordinal)
                || stored.LeaseExpiresUtc <= nowUtc)
            {
                return Task.FromResult(false);
            }

            _items[job.Id] = Clone(job);
            return Task.FromResult(true);
        }
    }

    public Task<PublishJobRetryResult> RetryOrFailLeasedAsync(
        Guid jobId,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        DateTime nextAttemptUtc,
        int maxAttempts,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        lock (_activeJobGate)
        {
            if (!_items.TryGetValue(jobId, out var job)
                || job.Status != PublishJobStatus.Running
                || job.LeaseToken != leaseToken
                || !string.Equals(job.LeaseOwner, owner, StringComparison.Ordinal)
                || job.LeaseExpiresUtc <= nowUtc)
            {
                return Task.FromResult(new PublishJobRetryResult(PublishJobRetryDisposition.LeaseLost, null));
            }

            PublishJobRetryDisposition disposition;
            if (job.AttemptCount >= maxAttempts)
            {
                job.MarkFailed(failureReason);
                disposition = PublishJobRetryDisposition.Failed;
            }
            else
            {
                job.ScheduleRetry(leaseToken, owner, failureReason, nextAttemptUtc);
                disposition = PublishJobRetryDisposition.RetryScheduled;
            }

            _items[job.Id] = Clone(job);
            return Task.FromResult(new PublishJobRetryResult(disposition, Clone(job)));
        }
    }

    public Task<IReadOnlyCollection<PublishJob>> RecoverExpiredLeasesAsync(
        DateTime nowUtc,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        lock (_activeJobGate)
        {
            var normalizedReason = failureReason.Length <= 1024 ? failureReason : failureReason[..1024];
            var expired = _items.Values
                .Where(job => job.Status == PublishJobStatus.Running
                    && (!job.LeaseExpiresUtc.HasValue || job.LeaseExpiresUtc <= nowUtc))
                .OrderBy(job => job.LeaseExpiresUtc)
                .ThenBy(job => job.CreatedUtc)
                .ToArray();

            foreach (var job in expired)
            {
                job.MarkFailed(normalizedReason);
                _items[job.Id] = Clone(job);
            }

            return Task.FromResult<IReadOnlyCollection<PublishJob>>(expired.Select(Clone).ToArray());
        }
    }

    public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PublishJob> items = _items.Values
            .OrderBy(x => x.CreatedUtc)
            .Select(Clone)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PublishJob> items = _items.Values
            .Where(x => IsActiveStatus(x.Status))
            .OrderBy(x => x.CreatedUtc)
            .Select(Clone)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.Values.Count(job => job.Status == PublishJobStatus.Pending));
    }

    public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filtered = _items.Values
            .Where(x => x.ApplicationId == query.ApplicationId)
            .Where(x => string.IsNullOrWhiteSpace(query.SequenceNumber) || x.SequenceNumber == query.SequenceNumber)
            .Where(x => string.IsNullOrWhiteSpace(query.Status) || x.Status.ToString().Equals(query.Status, StringComparison.OrdinalIgnoreCase))
            .Where(x => !query.CreatedFromUtc.HasValue || x.CreatedUtc >= query.CreatedFromUtc.Value)
            .Where(x => !query.CreatedToUtc.HasValue || x.CreatedUtc <= query.CreatedToUtc.Value)
            .Where(x => MatchesReadinessStatus(x.Id, query.ReadinessStatus))
            .OrderByDescending(x => x.CreatedUtc)
            .ToArray();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(Clone).ToArray();
        var statusCounts = PublishJobHistoryStatusCounts.Create(filtered);
        var pageSummaries = new Dictionary<Guid, PublishJobHistorySummary>();
        foreach (var job in pageItems)
        {
            if (_historySummaries.TryGetValue(job.Id, out var summary))
            {
                pageSummaries[job.Id] = summary;
            }
        }
        var summaries = filtered
            .Select(job => _historySummaries.GetValueOrDefault(job.Id))
            .ToArray();

        return Task.FromResult(new PublishJobHistoryQueryResult(
            pageItems,
            statusCounts.Total,
            statusCounts.Completed,
            statusCounts.Failed,
            statusCounts.Running,
            pageSummaries,
            new PublishJobHistoryReadinessCounts(
                summaries.Count(summary => string.Equals(summary?.ReadinessStatus, "Ready", StringComparison.Ordinal)),
                summaries.Count(summary => string.Equals(summary?.ReadinessStatus, "Blocked", StringComparison.Ordinal)),
                summaries.Count(summary => summary?.ReadinessStatus is null)),
            new PublishJobHistoryLifecycleCounts(
                summaries.Sum(summary => summary?.LifecycleMatchedCount ?? 0),
                summaries.Sum(summary => summary?.LifecycleReplaceTargetNotFoundCount ?? 0),
                summaries.Sum(summary => summary?.LifecycleDeleteTargetNotFoundCount ?? 0),
                summaries.Sum(summary => summary?.LifecycleAppendTargetNotFoundCount ?? 0),
                summaries.Sum(summary => summary?.LifecycleAmbiguousCount ?? 0),
                summaries.Sum(summary => summary?.LifecycleCurrentSequenceCount ?? 0))));
    }

    public Task DeleteByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var keys = _items.Values
            .Where(x => x.ApplicationId == applicationId)
            .Select(x => x.Id)
            .ToArray();

        foreach (var key in keys)
        {
            _items.TryRemove(key, out _);
            _historySummaries.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task DeleteBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var keys = _items.Values
            .Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber)
            .Select(x => x.Id)
            .ToArray();

        foreach (var key in keys)
        {
            _items.TryRemove(key, out _);
            _historySummaries.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private bool MatchesReadinessStatus(Guid jobId, string? requestedStatus)
    {
        if (string.IsNullOrWhiteSpace(requestedStatus))
        {
            return true;
        }

        _historySummaries.TryGetValue(jobId, out var summary);
        if (requestedStatus.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return summary?.ReadinessStatus is null;
        }

        return string.Equals(summary?.ReadinessStatus, requestedStatus.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static PublishJob Clone(PublishJob job)
        => PublishJob.Rehydrate(
            job.Id,
            job.ApplicationId,
            job.SequenceNumber,
            job.Status,
            job.OutputPath,
            job.PackagePath,
            job.CreatedUtc,
            job.CompletedUtc,
            job.FailureReason,
            job.IdempotencyKey,
            job.AttemptCount,
            job.NextAttemptUtc,
            job.LeaseOwner,
            job.LeaseToken,
            job.LeaseExpiresUtc,
            job.LastHeartbeatUtc);
}
