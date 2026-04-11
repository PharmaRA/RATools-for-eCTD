using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryDocumentPlacementRepository : IDocumentPlacementRepository
{
    private readonly ConcurrentDictionary<Guid, DocumentPlacement> _items = new();

    public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
    {
        _items[placement.Id] = placement;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _items.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _items.TryGetValue(id, out var placement);
        return Task.FromResult(placement);
    }

    public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DocumentPlacement> items = _items.Values
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DocumentPlacement> items = _items.Values
            .Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DocumentPlacement> items = _items.Values
            .Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber)
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }
}
