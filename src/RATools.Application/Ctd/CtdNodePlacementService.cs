using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;
using RATools.Application.Workspaces;
using RATools.Domain.Documents;
using RATools.Domain.Ctd;
using System.Security.Cryptography;

namespace RATools.Application.Ctd;

public sealed record NodePlacementMoveRequest(Guid NodeInstanceId, int SortOrder, long? ExpectedRevision, string? CtdSection = null);
public sealed record NodePlacementMovePreview(Guid PlacementId, Guid NodeInstanceId, string SourcePath, string TargetPath, long WorkspaceRevision)
{
    internal RATools.Application.Documents.Dtos.DocumentPlacementDto? Placement { get; init; }
}

public sealed class CtdNodePlacementService(ICtdNodeRepository nodes, IDocumentPlacementRepository placements,
    IDocumentRepository documents, IApplicationRepository applications, IDocumentStorageBoundary boundary,
    CtdNodePathResolver paths, IFileStorage files, WorkspaceMutationCoordinator mutations, NodeFileMoveJournal journal)
{
    public async Task<NodePlacementMovePreview> MoveAsync(Guid placementId, NodePlacementMoveRequest request,
        bool previewOnly = false, CancellationToken cancellationToken = default)
    {
        var initial = await placements.GetAsync(placementId, cancellationToken)
            ?? throw new InvalidOperationException("The placement does not exist.");
        await using var mutation = await mutations.AcquireAsync(initial.ApplicationId, initial.SequenceNumber, request.ExpectedRevision, cancellationToken);
        var original = await placements.GetAsync(placementId, cancellationToken)
            ?? throw new InvalidOperationException("The placement no longer exists.");
        var application = await applications.GetAsync(original.ApplicationId, cancellationToken)
            ?? throw new WorkspaceRevisionTargetNotFoundException();
        var workspace = await nodes.GetSequenceAsync(original.ApplicationId, original.SequenceNumber, cancellationToken)
            ?? throw new WorkspaceRevisionTargetNotFoundException();
        if (journal.Read(application).Count > 0)
            throw new InvalidOperationException("A pending node move requires recovery before editing this workspace.");
        var targetNode = workspace.Nodes.SingleOrDefault(node => node.NodeInstanceId == request.NodeInstanceId)
            ?? throw new InvalidOperationException("Select a node in the placement's sequence.");
        if (request.CtdSection is not null && request.CtdSection.Trim() != targetNode.CtdSection)
            throw new CtdNodeConstraintException("PlacementNodeSectionMismatch", "The supplied section differs from the target node definition.", request.NodeInstanceId);
        var updated = DocumentPlacement.Rehydrate(original.Id, original.DocumentId, original.ApplicationId, original.SequenceNumber,
            original.CtdSection, original.Operation, original.Title, original.LifecycleTargetPlacementId, original.CreatedUtc,
            original.LeafId, original.NodeInstanceId, original.SortOrder, original.ImportedSource);
        updated.BindToNode(targetNode, request.SortOrder);
        if (original.LifecycleTargetPlacementId is { } targetId)
        {
            var historical = await placements.GetAsync(targetId, cancellationToken);
            if (historical is null || historical.ApplicationId != original.ApplicationId ||
                string.CompareOrdinal(historical.SequenceNumber, original.SequenceNumber) >= 0 ||
                historical.NodeInstanceId != request.NodeInstanceId)
                throw new InvalidOperationException("The lifecycle target belongs to a different business instance. Resolve the target before moving.");
        }
        var allPlacements = await placements.ListAsync(cancellationToken);
        if (original.NodeInstanceId != request.NodeInstanceId &&
            (original.Operation != DocumentPlacementOperation.New || allPlacements.Any(item => item.LifecycleTargetPlacementId == placementId)))
            throw new InvalidOperationException("A lifecycle operation or referenced historical leaf cannot be moved to a different business instance.");
        var document = await documents.GetAsync(original.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("The document does not exist.");
        // Reordering a bound leaf never normalizes an imported href, and delete leaves need no new physical file.
        if (original.NodeInstanceId == request.NodeInstanceId)
        {
            if (!previewOnly && original.SortOrder != request.SortOrder)
                await mutation.CommitAsync(async ct =>
                {
                    if (!await placements.UpdateAsync(updated, ct)) throw new InvalidOperationException("The placement could not be updated.");
                }, cancellationToken);
            return new(placementId, request.NodeInstanceId, document.StoragePath, document.StoragePath, mutation.Revision)
            {
                Placement = updated.ToDto() with { WorkspaceRevision = mutation.Revision }
            };
        }
        var source = boundary.EnsureDocumentOwnedBySequence(document, application, original.SequenceNumber);
        if (!File.Exists(source)) throw new FileNotFoundException("The source workspace file is missing.", source);
        if (allPlacements.Any(item => item.DocumentId == document.Id && item.Id != placementId))
            throw new InvalidOperationException("A shared document cannot be moved independently.");
        paths.ValidateAllocation(application.EctdTemplateKey, workspace);
        var relative = paths.ResolveFile(application.EctdTemplateKey, workspace, request.NodeInstanceId, Path.GetFileName(source));
        var destination = boundary.EnsurePathOwnedBySequence(
            Path.Combine(application.WorkingDirectoryPath, original.SequenceNumber, relative.Replace('/', Path.DirectorySeparatorChar)),
            application, original.SequenceNumber);
        var movesFile = !string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
        if ((movesFile && (File.Exists(destination) || Directory.Exists(destination))) ||
            (await documents.ListAsync(cancellationToken)).Any(item => item.Id != document.Id && string.Equals(item.StoragePath, destination, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("The destination path is already occupied.");
        EnsurePortableDestination(application.WorkingDirectoryPath, destination, movesFile ? null : source);
        var result = new NodePlacementMovePreview(placementId, request.NodeInstanceId, source, destination, mutation.Revision);
        if (previewOnly) return result;
        NodeFileMoveIntent? intent = null;
        if (movesFile)
        {
            await using var stream = File.OpenRead(source);
            intent = new(placementId, document.Id, original.ApplicationId, original.SequenceNumber, mutation.Revision,
                original.NodeInstanceId, original.CtdSection, original.SortOrder, request.NodeInstanceId,
                updated.CtdSection, updated.SortOrder, source, destination, stream.Length,
                Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)));
            journal.Prepare(application, intent);
        }
        var moved = false;
        try
        {
            if (movesFile)
            {
                await files.RenameAsync(source, destination, cancellationToken);
                moved = true;
            }
            document.Relocate(destination);
            await mutation.CommitAsync(async ct =>
            {
                if (!await documents.UpdateAsync(document, ct) || !await placements.UpdateAsync(updated, ct))
                    throw new InvalidOperationException("The document or placement could not be updated.");
            }, cancellationToken);
        }
        catch (Exception failure)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // A storage implementation can throw after moving the file.
            moved |= movesFile && !File.Exists(source) && File.Exists(destination);
            var compensationFailures = new List<Exception>();
            if (moved)
            {
                try { await files.RenameAsync(destination, source, cleanup.Token); moved = false; }
                catch (Exception error) { compensationFailures.Add(error); }
            }
            // Rename may have completed before throwing, including during compensation.
            moved = movesFile && !File.Exists(source) && File.Exists(destination);
            if (movesFile && File.Exists(source) == File.Exists(destination))
                throw new InvalidOperationException("Node move recovery is required: physical file state is ambiguous.", failure);
            document.Relocate(moved ? destination : source);
            var reconciled = false;
            try
            {
                await mutation.ReconcileAsync(moved, async ct =>
                {
                    if (!await documents.UpdateAsync(document, ct) || !await placements.UpdateAsync(moved ? updated : original, ct))
                        throw new InvalidOperationException("Node move compensation was incomplete.");
                }, cleanup.Token);
                reconciled = true;
                if (intent is not null) journal.Complete(application, placementId);
            }
            catch (Exception error) { compensationFailures.Add(error); }
            if (compensationFailures.Count > 0)
                throw new InvalidOperationException(reconciled
                    ? "The file state, node binding and revision were reconciled after the failed move."
                    : "Node move compensation was incomplete; the durable recovery record was retained.",
                    new AggregateException([failure, .. compensationFailures]));
            throw;
        }
        if (intent is not null) journal.Complete(application, placementId);
        return result with { WorkspaceRevision = mutation.Revision, Placement = updated.ToDto() with { WorkspaceRevision = mutation.Revision } };
    }

    // A workspace prepared on Linux must also be safe when delivered on Windows.
    private static void EnsurePortableDestination(string root, string destination, string? sameFile)
    {
        var current = Path.GetFullPath(root);
        var parts = Path.GetRelativePath(current, destination).Split(Path.DirectorySeparatorChar);
        for (var index = 0; index < parts.Length && Directory.Exists(current); index++)
        {
            var entry = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), parts[index], StringComparison.OrdinalIgnoreCase));
            if (entry is null) return;
            if (!string.Equals(Path.GetFileName(entry), parts[index], StringComparison.Ordinal) ||
                (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0 ||
                (index < parts.Length - 1 && !Directory.Exists(entry)) ||
                (index == parts.Length - 1 && !string.Equals(entry, sameFile, StringComparison.Ordinal)))
                throw new CtdNodeConstraintException("NodePathConflict", "The destination conflicts with an existing file, directory, case variant or link.");
            current = entry;
        }
    }
}
