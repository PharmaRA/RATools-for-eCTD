using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents;
using RATools.Application.Documents.Dtos;

namespace RATools.Application.Workspaces;

public sealed record WorkspaceSnapshotDto(Guid ApplicationId, string SequenceNumber, long WorkspaceRevision,
    IReadOnlyCollection<DocumentPlacementDto> Placements, IReadOnlyCollection<DocumentDto> Documents);

public sealed class WorkspaceSnapshotService(IWorkspaceRevisionStore revisions,
    IDocumentPlacementService placements, IDocumentService documents, RATools.Application.Ctd.NodeFileMoveGuard? moveGuard = null)
{
    public async Task<WorkspaceSnapshotDto?> GetAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        await using var applicationLock = await revisions.LockApplicationAsync(applicationId, cancellationToken);
        if (moveGuard is not null) await moveGuard.EnsureReadyAsync(applicationId, cancellationToken);
        var revision = await revisions.GetRevisionAsync(applicationId, sequenceNumber, cancellationToken);
        if (revision is null) return null;
        // Include application history for lifecycle target selection, while the
        // revision protects the displayed draft. Reads share the writers' lock.
        var applicationPlacements = await placements.ListByApplicationAsync(applicationId, cancellationToken);
        var applicationDocuments = await documents.ListByApplicationAsync(applicationId, null, cancellationToken);
        return new WorkspaceSnapshotDto(applicationId, sequenceNumber, revision.Value, applicationPlacements, applicationDocuments);
    }
}
