using System.Security.Cryptography;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;

namespace RATools.Application.Ctd;

/// <summary>Runs before accepting edits or publishing after restart. Database state determines the retained path.</summary>
public sealed class NodeFileMoveRecovery(IApplicationRepository applications, IDocumentRepository documents,
    IDocumentPlacementRepository placements, IWorkspaceRevisionStore revisions, IDocumentStorageBoundary boundary,
    IFileStorage files, NodeFileMoveJournal journal)
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        foreach (var application in await applications.ListAsync(cancellationToken))
        {
            await using var heldLock = await revisions.LockApplicationAsync(application.Id, cancellationToken);
            foreach (var intent in journal.Read(application))
            {
                var source = boundary.EnsurePathOwnedBySequence(intent.SourcePath, application, intent.SequenceNumber);
                var target = boundary.EnsurePathOwnedBySequence(intent.TargetPath, application, intent.SequenceNumber);
                var placement = await placements.GetAsync(intent.PlacementId, cancellationToken);
                var document = await documents.GetAsync(intent.DocumentId, cancellationToken);
                var revision = await revisions.GetRevisionAsync(application.Id, intent.SequenceNumber, cancellationToken);
                if (placement is null || document is null || placement.DocumentId != intent.DocumentId ||
                    placement.ApplicationId != application.Id || placement.SequenceNumber != intent.SequenceNumber ||
                    revision is null || revision < intent.ExpectedRevision)
                    throw new InvalidOperationException("A pending node move no longer matches its workspace records. Recovery requires inspection.");
                var isOriginal = placement.NodeInstanceId == intent.SourceNodeId && placement.CtdSection == intent.SourceSection &&
                    placement.SortOrder == intent.SourceSortOrder && document.StoragePath == source;
                var isTarget = placement.NodeInstanceId == intent.TargetNodeId && placement.CtdSection == intent.TargetSection &&
                    placement.SortOrder == intent.TargetSortOrder && document.StoragePath == target && revision > intent.ExpectedRevision;
                if (!isOriginal && !isTarget)
                    throw new InvalidOperationException("A pending node move has inconsistent database state. Recovery requires inspection.");
                if (File.Exists(source) == File.Exists(target))
                    throw new InvalidOperationException("A pending node move has missing or duplicate files. Recovery will not overwrite either path.");
                var existing = File.Exists(source) ? source : target;
                await using (var stream = File.OpenRead(existing))
                {
                    if (stream.Length != intent.Length || !string.Equals(
                        Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)), intent.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A pending node move file differs from its prepared content. Recovery requires inspection.");
                }
                var retained = isOriginal ? source : target;
                if (!string.Equals(existing, retained, StringComparison.Ordinal))
                    await files.RenameAsync(existing, retained, cancellationToken);
                journal.Complete(application, intent.PlacementId);
            }
        }
    }
}
