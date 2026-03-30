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
}
