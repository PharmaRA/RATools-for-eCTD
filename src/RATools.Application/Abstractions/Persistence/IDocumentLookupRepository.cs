using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Persistence;

public interface IDocumentLookupRepository
{
    Task<IReadOnlyCollection<SubmissionDocument>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
