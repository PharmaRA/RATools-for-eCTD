using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Persistence;

internal static class DocumentRepositoryLookupExtensions
{
    public static async Task<IReadOnlyCollection<SubmissionDocument>> ListByIdsPreferScopedAsync(
        this IDocumentRepository repository,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var distinctIds = ids.Distinct().ToArray();
        if (repository is IDocumentLookupRepository lookupRepository)
        {
            return await lookupRepository.ListByIdsAsync(distinctIds, cancellationToken);
        }

        var idSet = distinctIds.ToHashSet();
        var allDocuments = await repository.ListAsync(cancellationToken);
        return allDocuments.Where(x => idSet.Contains(x.Id)).ToArray();
    }
}
