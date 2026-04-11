using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Persistence;

public interface IDocumentPlacementRepository
{
    Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(
        Guid applicationId,
        string sequenceNumber,
        CancellationToken cancellationToken = default);
}
