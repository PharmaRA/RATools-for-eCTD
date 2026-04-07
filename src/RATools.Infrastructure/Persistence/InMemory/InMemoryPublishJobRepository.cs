using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryPublishJobRepository : IPublishJobRepository
{
    private readonly ConcurrentDictionary<Guid, PublishJob> _items = new();

    public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        _items[job.Id] = job;
        return Task.CompletedTask;
    }

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

        return Task.FromResult(new PublishJobHistoryQueryResult(
            pageItems,
            filtered.Length,
            filtered.Count(x => x.Status == PublishJobStatus.Completed),
            filtered.Count(x => x.Status == PublishJobStatus.Failed),
            filtered.Count(x => x.Status == PublishJobStatus.Running)));
    }
}
