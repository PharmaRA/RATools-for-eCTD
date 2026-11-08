using RATools.Tests.PackageValidation;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Application.Ctd;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Workspaces;

[Trait("Category", "PathSecurity")]
public sealed class CtdNodeMoveTests
{
    [Fact]
    public async Task CompletedJournalRetainsExactBytesAndRepeatedCompletionIsNoOp()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync(); var item = await scope.AddAsync(node: scope.Alpha);
        scope.Prepare(item.Document, item.Placement);
        var source = JournalPath(scope, item.Placement.Id);
        var original = await File.ReadAllBytesAsync(source);
        scope.Journal.Complete(scope.Application, item.Placement.Id);
        scope.Journal.Complete(scope.Application, item.Placement.Id);
        Assert.False(File.Exists(source)); Assert.Empty(scope.Journal.Read(scope.Application));
        var retained = Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(source)!, "_recycle")));
        Assert.Equal(original, await File.ReadAllBytesAsync(retained));
    }

    [Fact]
    public async Task BlockedJournalRecycleKeepsPendingRecordAndExactBytes()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync(); var item = await scope.AddAsync(node: scope.Alpha);
        var intent = scope.Prepare(item.Document, item.Placement);
        var source = JournalPath(scope, item.Placement.Id);
        var original = await File.ReadAllBytesAsync(source);
        var recycle = Path.Combine(Path.GetDirectoryName(source)!, "_recycle");
        await File.WriteAllTextAsync(recycle, "blocked");
        Assert.Throws<IOException>(() => scope.Journal.Complete(scope.Application, item.Placement.Id));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Equal(intent, Assert.Single(scope.Journal.Read(scope.Application)));
        Assert.Equal("blocked", await File.ReadAllTextAsync(recycle));
    }

    [Fact]
    public void MissingJournalCompletionDoesNotCreateStorage()
    {
        using var scope = new NodeMoveTestWorkspace();
        scope.Journal.Complete(scope.Application, Guid.NewGuid());
        Assert.Empty(Directory.GetFileSystemEntries(scope.Root));
    }

    [Fact]
    public async Task CommittedMoveWithBlockedJournalRecycleCanRecoverWithoutReversingMove()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync(); var item = await scope.AddAsync(node: scope.Alpha);
        var journalDirectory = Path.GetDirectoryName(JournalPath(scope, item.Placement.Id))!;
        Directory.CreateDirectory(journalDirectory);
        var blocker = Path.Combine(journalDirectory, "_recycle"); await File.WriteAllTextAsync(blocker, "blocked");
        await Assert.ThrowsAsync<IOException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        var intent = Assert.Single(scope.Journal.Read(scope.Application));
        Assert.True(File.Exists(intent.TargetPath)); Assert.False(File.Exists(intent.SourcePath));
        Assert.Equal(2, await scope.RevisionAsync());
        Assert.Equal(scope.Beta.NodeInstanceId, (await scope.Placements.GetAsync(item.Placement.Id))!.NodeInstanceId);
        var original = await File.ReadAllBytesAsync(JournalPath(scope, item.Placement.Id));
        RetainedPackageTestFiles.RetainFile(scope.Root, blocker);
        await scope.Recovery().RecoverAsync(); await scope.Recovery().RecoverAsync();
        Assert.Empty(scope.Journal.Read(scope.Application)); Assert.True(File.Exists(intent.TargetPath));
        Assert.Equal(2, await scope.RevisionAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(blocker))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalCompletionRejectsSourceAndRecycleLinks(bool recycleLink)
    {
        using var scope = new NodeMoveTestWorkspace(); using var outside = new NodeMoveTestWorkspace();
        await scope.SeedAsync(); var item = await scope.AddAsync(node: scope.Alpha);
        scope.Prepare(item.Document, item.Placement);
        var source = JournalPath(scope, item.Placement.Id); var original = await File.ReadAllBytesAsync(source);
        var target = Path.Combine(outside.Root, "sentinel.json"); await File.WriteAllBytesAsync(target, original);
        var link = recycleLink ? Path.Combine(Path.GetDirectoryName(source)!, "_recycle") : source;
        if (!recycleLink) RetainedPackageTestFiles.RetainFile(scope.Root, source);
        using var ownedLink = RetainedPackageTestFiles.CreateOwnedLink(scope.Root, link, outside.Root,
            recycleLink ? outside.Root : target, directory: recycleLink);
        Assert.Throws<InvalidOperationException>(() => scope.Journal.Complete(scope.Application, item.Placement.Id));
        Assert.Equal(original, await File.ReadAllBytesAsync(target));
        Assert.Single(Directory.GetFileSystemEntries(outside.Root));
        if (recycleLink) Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    private static string JournalPath(NodeMoveTestWorkspace scope, Guid placementId) =>
        Path.Combine(scope.Root, ".ratools", "node-moves", placementId.ToString("N") + ".json");

    [Fact]
    public async Task PreviewDoesNotWriteAndSameNamedFilesRemainDistinct()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var first = await scope.AddAsync("first");
        var second = await scope.AddAsync("second");
        first.Placement.PreserveImportedSource(new("index.xml", "first/specification.pdf", null), 0);
        await scope.Placements.UpdateAsync(first.Placement);
        var preview = await scope.Service.MoveAsync(first.Placement.Id, new(scope.Alpha.NodeInstanceId, 4, 1), previewOnly: true);
        Assert.Equal(first.Document.StoragePath, preview.SourcePath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(preview.TargetPath)));
        Assert.Equal(1, await scope.RevisionAsync());
        await scope.Service.MoveAsync(first.Placement.Id, new(scope.Alpha.NodeInstanceId, 4, 1));
        await scope.Service.MoveAsync(second.Placement.Id, new(scope.Beta.NodeInstanceId, 5, 2));
        Assert.Equal("Synthetic PDF payload first", await File.ReadAllTextAsync(scope.Destination(scope.Alpha.NodeInstanceId)));
        Assert.Equal("Synthetic PDF payload second", await File.ReadAllTextAsync(scope.Destination(scope.Beta.NodeInstanceId)));
        Assert.Equal(first.Placement.LeafId, (await scope.Placements.GetAsync(first.Placement.Id))!.LeafId);
        Assert.Equal(first.Placement.ImportedSource, (await scope.Placements.GetAsync(first.Placement.Id))!.ImportedSource);
        Assert.Equal(3, await scope.RevisionAsync());
        Assert.Empty(scope.Journal.Read(scope.Application));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("ancestor-file")]
    [InlineData("case")]
    [InlineData("record")]
    public async Task ConflictsAreRejectedBeforeMoving(string conflict)
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        var destination = scope.Destination(scope.Beta.NodeInstanceId);
        if (conflict == "ancestor-file")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(destination))!);
            await File.WriteAllTextAsync(Path.GetDirectoryName(destination)!, "occupied");
        }
        else if (conflict == "record")
            await scope.Documents.AddAsync(new SubmissionDocument("specification.pdf", "application/pdf", 1, "sha", "md5", destination));
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (conflict == "directory") Directory.CreateDirectory(destination);
            else await File.WriteAllTextAsync(conflict == "case" ? Path.Combine(Path.GetDirectoryName(destination)!, "SPECIFICATION.PDF") : destination, "occupied");
        }
        await Assert.ThrowsAnyAsync<Exception>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        Assert.True(File.Exists(item.Document.StoragePath));
        Assert.Equal(scope.Alpha.NodeInstanceId, (await scope.Placements.GetAsync(item.Placement.Id))!.NodeInstanceId);
        Assert.Equal(1, await scope.RevisionAsync());
        Assert.Empty(scope.Journal.Read(scope.Application));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseFailureRestoresOriginalStateOrRetainsConsistentMovedState(bool failRestore)
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        scope.Placements.FailuresRemaining = 1;
        scope.Files.ThrowBeforeRename = failRestore ? 2 : 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        var document = await scope.Documents.GetAsync(item.Document.Id);
        var placement = await scope.Placements.GetAsync(item.Placement.Id);
        Assert.Equal(failRestore ? scope.Destination(scope.Beta.NodeInstanceId) : item.Document.StoragePath, document!.StoragePath);
        Assert.True(File.Exists(document.StoragePath));
        Assert.Equal(failRestore ? scope.Beta.NodeInstanceId : scope.Alpha.NodeInstanceId, placement!.NodeInstanceId);
        Assert.Equal(failRestore ? 7 : 3, placement.SortOrder);
        Assert.Equal(item.Placement.LeafId, placement.LeafId);
        Assert.Equal(failRestore ? 2 : 1, await scope.RevisionAsync());
        Assert.Empty(scope.Journal.Read(scope.Application));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageFailureAfterRenameAndCancellationUseIndependentCleanup(bool cancel)
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        using var cancellation = new CancellationTokenSource();
        if (cancel) scope.Files.CancelAfterMove = cancellation;
        else scope.Files.ThrowAfterRename = 1;
        await Assert.ThrowsAnyAsync<Exception>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1), cancellationToken: cancellation.Token));
        Assert.True(File.Exists(item.Document.StoragePath));
        Assert.False(File.Exists(scope.Destination(scope.Beta.NodeInstanceId)));
        Assert.Equal(item.Document.StoragePath, (await scope.Documents.GetAsync(item.Document.Id))!.StoragePath);
        Assert.Equal(1, await scope.RevisionAsync());
        Assert.Empty(scope.Journal.Read(scope.Application));
    }

    [Fact]
    public async Task CompensationThrowingAfterPhysicalRestoreKeepsOriginalBinding()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        scope.Placements.FailuresRemaining = 1;
        scope.Files.ThrowAfterRename = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        Assert.True(File.Exists(item.Document.StoragePath));
        Assert.Equal(scope.Alpha.NodeInstanceId, (await scope.Placements.GetAsync(item.Placement.Id))!.NodeInstanceId);
        Assert.Equal(1, await scope.RevisionAsync());
    }

    [Fact]
    public async Task SameInstanceReorderPreservesImportedHrefAndDeleteNeedsNoFile()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        item.Placement.ReviseOperation(DocumentPlacementOperation.Delete);
        await scope.Placements.UpdateAsync(item.Placement);
        RetainedPackageTestFiles.RetainFile(scope.Root, item.Document.StoragePath);
        var result = await scope.Service.MoveAsync(item.Placement.Id, new(scope.Alpha.NodeInstanceId, 7, 1));
        Assert.Equal(item.Document.StoragePath, result.TargetPath);
        Assert.Equal(2, result.WorkspaceRevision);
        Assert.Equal(7, (await scope.Placements.GetAsync(item.Placement.Id))!.SortOrder);
        Assert.False(File.Exists(result.TargetPath));
    }

    [Fact]
    public async Task StalePreviewCannotOverwriteACommittedMove()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        await scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1), previewOnly: true);
        await scope.Service.MoveAsync(item.Placement.Id, new(scope.Alpha.NodeInstanceId, 9, 1));
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        await Assert.ThrowsAsync<WorkspaceRevisionRequiredException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, null)));
        Assert.True(File.Exists(item.Document.StoragePath));
    }

    [Fact]
    public async Task InvalidScopeContainerAndConflictingSectionAreRejected()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.MoveAsync(item.Placement.Id, new(Guid.NewGuid(), 0, 1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, -1, 1)));
        await Assert.ThrowsAsync<CtdNodeConstraintException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 0, 1, "m2.5")));
        Assert.Equal(1, await scope.RevisionAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartRecoveryUsesCommittedDatabaseState(bool committed)
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        var intent = scope.Prepare(item.Document, item.Placement);
        await scope.Files.RenameAsync(intent.SourcePath, intent.TargetPath);
        if (committed)
        {
            await using var mutation = await scope.Mutations.AcquireAsync(scope.Application.Id, "0000", 1);
            await mutation.CommitAsync(async ct =>
            {
                item.Document.Relocate(intent.TargetPath);
                item.Placement.BindToNode(scope.Beta, 7);
                await scope.Documents.UpdateAsync(item.Document, ct);
                await scope.Placements.UpdateAsync(item.Placement, ct);
            });
        }
        await scope.Recovery().RecoverAsync();
        await scope.Recovery().RecoverAsync();
        Assert.True(File.Exists(committed ? intent.TargetPath : intent.SourcePath));
        Assert.False(File.Exists(committed ? intent.SourcePath : intent.TargetPath));
        Assert.Equal(committed ? 2 : 1, await scope.RevisionAsync());
        Assert.Empty(scope.Journal.Read(scope.Application));
    }

    [Fact]
    public async Task RecoveryRefusesChangedContentAndRetainsEvidence()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        var intent = scope.Prepare(item.Document, item.Placement);
        await scope.Files.RenameAsync(intent.SourcePath, intent.TargetPath);
        await File.WriteAllTextAsync(intent.TargetPath, "Changed outside the application");
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Recovery().RecoverAsync());
        Assert.Single(scope.Journal.Read(scope.Application));
        Assert.True(File.Exists(intent.TargetPath));
    }

    [Fact]
    public async Task PendingRecoveryBlocksOtherWorkspaceMutations()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        scope.Prepare(item.Document, item.Placement);
        var coordinator = new WorkspaceMutationCoordinator(scope.Revisions, new InMemoryPersistenceTransaction(), new NodeFileMoveGuard(scope.Applications, scope.Journal));
        var exception = await Assert.ThrowsAsync<CtdNodeConstraintException>(() => coordinator.AcquireAsync(scope.Application.Id, "0000", 1));
        Assert.Equal("NodeMoveRecoveryRequired", exception.Code);
        await scope.Recovery().RecoverAsync();
        await using var retry = await coordinator.AcquireAsync(scope.Application.Id, "0000", 1);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("shared")]
    [InlineData("unresolved-lifecycle")]
    [InlineData("referenced-leaf")]
    public async Task MissingSharedAndLifecycleDocumentsCannotBeMovedToAnotherInstance(string reason)
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        if (reason == "missing") RetainedPackageTestFiles.RetainFile(scope.Root, item.Document.StoragePath);
        if (reason == "unresolved-lifecycle")
        {
            item.Placement.ReviseOperation(DocumentPlacementOperation.Replace);
            await scope.Placements.UpdateAsync(item.Placement);
        }
        if (reason is "shared" or "referenced-leaf")
        {
            var other = new DocumentPlacement(item.Document.Id, scope.Application.Id, "0000", "m3.2.s.4.1", DocumentPlacementOperation.New, "Other");
            if (reason == "referenced-leaf") other.ReviseLifecycleTarget(item.Placement.Id);
            await scope.Placements.AddAsync(other);
        }
        await Assert.ThrowsAnyAsync<Exception>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        Assert.Equal(scope.Alpha.NodeInstanceId, (await scope.Placements.GetAsync(item.Placement.Id))!.NodeInstanceId);
        Assert.Equal(1, await scope.RevisionAsync());
        Assert.Empty(scope.Journal.Read(scope.Application));
    }

    [Fact]
    public async Task IncompleteDatabaseCompensationRetainsJournalForRecovery()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        scope.Placements.FailuresRemaining = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        Assert.Single(scope.Journal.Read(scope.Application));
        await scope.Recovery().RecoverAsync();
        Assert.True(File.Exists(item.Document.StoragePath));
        Assert.Empty(scope.Journal.Read(scope.Application));
        Assert.Equal(1, await scope.RevisionAsync());
    }

    [Fact]
    public async Task BindingAnAlreadyPlacedFileRejectsAnotherDocumentUsingItsHref()
    {
        using var scope = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var relative = Path.GetDirectoryName(Path.GetRelativePath(Path.Combine(scope.Root, "0000"), scope.Destination(scope.Beta.NodeInstanceId)))!;
        var item = await scope.AddAsync(relative);
        await scope.Documents.AddAsync(new SubmissionDocument("specification.pdf", "application/pdf", 1, "sha", "md5", item.Document.StoragePath));
        await Assert.ThrowsAsync<IOException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 0, 1)));
        Assert.Null((await scope.Placements.GetAsync(item.Placement.Id))!.NodeInstanceId);
        Assert.True(File.Exists(item.Document.StoragePath));
        Assert.Equal(1, await scope.RevisionAsync());
    }

    [Fact]
    public async Task TargetDirectoryLinkCannotMoveOrModifyExternalFiles()
    {
        using var scope = new NodeMoveTestWorkspace();
        using var outside = new NodeMoveTestWorkspace();
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        var link = Path.GetDirectoryName(scope.Destination(scope.Beta.NodeInstanceId))!;
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var sentinel = Path.Combine(outside.Root, "specification.pdf");
        await File.WriteAllTextAsync(sentinel, "external");
        var ownedLink = RetainedPackageTestFiles.CreateOwnedLink(scope.Root, link, outside.Root, outside.Root);
        try
        {
            await Assert.ThrowsAsync<RATools.Application.Documents.DocumentStorageBoundaryException>(() =>
                scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 0, 1)));
            Assert.Equal("external", await File.ReadAllTextAsync(sentinel));
            Assert.True(File.Exists(item.Document.StoragePath));
        }
        finally { ownedLink.Dispose(); }
    }
}
