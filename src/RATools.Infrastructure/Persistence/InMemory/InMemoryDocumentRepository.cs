using RATools.Infrastructure.Persistence.EfCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryDocumentRepository : IDocumentRepository, IDocumentLookupRepository
{
    private readonly TransactionalMemoryCollection<SubmissionDocument> _items = new(item => item.ToRecord().ToDomain());

    public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
    {
        _items.Set(document.Id, document);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_items.Update(document.Id, document));
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _items.Remove(id);
        return Task.CompletedTask;
    }

    public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = _items.Get(id);
        return Task.FromResult(document);
    }

    public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<SubmissionDocument> items = _items.Values
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<IReadOnlyCollection<SubmissionDocument>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToHashSet();
        IReadOnlyCollection<SubmissionDocument> items = _items.Values
            .Where(x => idSet.Contains(x.Id))
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }
}
