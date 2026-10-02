using RATools.Application.Abstractions.Persistence;

namespace RATools.Application.Applications;

public sealed class ApplicationImportCleanupException : InvalidOperationException
{
    public ApplicationImportCleanupException(ApplicationImportBatch batch, PersistenceRollbackException failure)
        : base($"IMPORT_ROLLBACK_INCOMPLETE: Cleanup could not be confirmed for application '{batch.Application.Id}'. " +
            $"Inspect application, sequences, nodes, documents, placements, diagnostics and imported backbones. " +
            $"Document IDs: {string.Join(",", batch.Documents.Select(item => item.Id))}; " +
            $"placement IDs: {string.Join(",", batch.Placements.Select(item => item.Id))}.", failure)
    {
        ApplicationId = batch.Application.Id;
        UnconfirmedDocumentIds = batch.Documents.Select(item => item.Id).ToArray();
        UnconfirmedPlacementIds = batch.Placements.Select(item => item.Id).ToArray();
    }

    public Guid ApplicationId { get; }
    public IReadOnlyList<Guid> UnconfirmedDocumentIds { get; }
    public IReadOnlyList<Guid> UnconfirmedPlacementIds { get; }
}
