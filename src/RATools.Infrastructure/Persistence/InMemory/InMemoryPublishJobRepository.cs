using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryPublishJobRepository : IPublishJobRepository
{
    private readonly ConcurrentDictionary<Guid, PublishJob> _items = new();
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

            _items[job.Id] = job;
        }

        return Task.CompletedTask;
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
        _items[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _items.TryGetValue(id, out var job);
        return Task.FromResult(job);
    }

    public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PublishJob> items = _items.Values
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PublishJob> items = _items.Values
            .Where(x => IsActiveStatus(x.Status))
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = _items.Values
            .Where(x => x.ApplicationId == query.ApplicationId)
            .Where(x => string.IsNullOrWhiteSpace(query.SequenceNumber) || x.SequenceNumber == query.SequenceNumber)
            .Where(x => string.IsNullOrWhiteSpace(query.Status) || x.Status.ToString().Equals(query.Status, StringComparison.OrdinalIgnoreCase))
            .Where(x => !query.CreatedFromUtc.HasValue || x.CreatedUtc >= query.CreatedFromUtc.Value)
            .Where(x => !query.CreatedToUtc.HasValue || x.CreatedUtc <= query.CreatedToUtc.Value)
            .OrderByDescending(x => x.CreatedUtc)
            .ToArray();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var statusCounts = PublishJobHistoryStatusCounts.Create(filtered);

        return Task.FromResult(new PublishJobHistoryQueryResult(
            pageItems,
            statusCounts.Total,
            statusCounts.Completed,
            statusCounts.Failed,
            statusCounts.Running));
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
        }

        return Task.CompletedTask;
    }
}
