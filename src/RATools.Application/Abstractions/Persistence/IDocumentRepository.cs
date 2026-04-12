using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Persistence;

public interface IDocumentRepository
{
    Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default);
}
