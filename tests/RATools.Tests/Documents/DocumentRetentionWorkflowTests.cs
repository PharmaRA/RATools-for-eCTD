using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.Documents;

public sealed class DocumentRetentionWorkflowTests
{
    [Fact]
    public async Task DeletedDocumentRetainsOriginalBytesAndAdvancesRevision()
    {
        using var scope = new Scope(); await scope.SeedAsync();
        var document = await scope.AddDocumentAsync();
        Assert.True(await scope.Service().DeleteAsync(document.Id, 0));
        Assert.Null(await scope.Documents.GetAsync(document.Id));
        Assert.False(File.Exists(document.StoragePath));
        Assert.Equal("retained payload", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(
            Path.Combine(Path.GetDirectoryName(document.StoragePath)!, "_recycle")))));
        Assert.Equal(1, await scope.Revisions.GetRevisionAsync(scope.Application.Id, "0000"));
    }

    [Fact]
    public async Task SharedPayloadRemainsAtItsOriginalPath()
    {
        using var scope = new Scope(); await scope.SeedAsync();
        var document = await scope.AddDocumentAsync();
        var other = new SubmissionDocument("other.pdf", "application/pdf", 16, "sha", "md5", document.StoragePath);
        await scope.Documents.AddAsync(other);
        Assert.True(await scope.Service().DeleteAsync(document.Id, 0));
        Assert.NotNull(await scope.Documents.GetAsync(other.Id));
        Assert.Equal("retained payload", await File.ReadAllTextAsync(document.StoragePath));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(document.StoragePath)!, "_recycle")));
    }

    [Fact]
    public async Task BlockedDocumentRetentionKeepsThePayloadAfterDatabaseCommit()
    {
        using var scope = new Scope(); await scope.SeedAsync();
        var document = await scope.AddDocumentAsync();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(document.StoragePath)!, "_recycle"), "blocker");
        await Assert.ThrowsAsync<IOException>(() => scope.Service().DeleteAsync(document.Id, 0));
        Assert.Null(await scope.Documents.GetAsync(document.Id));
        Assert.Equal("retained payload", await File.ReadAllTextAsync(document.StoragePath));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task FailedUploadPreservesOriginalFailureAndBytes(bool committed, bool blocked, bool readbackFailure)
    {
        using var scope = new Scope(); await scope.SeedAsync();
        var repository = new FailedRegistration(scope.Documents, committed, readbackFailure);
        var folder = Path.Combine(scope.Application.WorkingDirectoryPath, "0000", "m1", "us", "12-cover-letters");
        if (blocked) { Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "_recycle"), "blocker"); }
        using var content = new MemoryStream("uploaded payload"u8.ToArray());
        var error = await Assert.ThrowsAsync<IOException>(() => scope.Service(repository).UploadToSequenceAsync(scope.Application.Id,
            "0000", new UploadSequenceDocumentRequest { FileName = "cover.pdf", MediaType = "application/pdf", CtdSection = "m1.2", ExpectedRevision = 0, Content = content }));
        Assert.Same(repository.Failure, error);
        var stored = Assert.IsType<SubmissionDocument>(repository.Attempted);
        Assert.Equal(committed || blocked || readbackFailure, File.Exists(stored.StoragePath));
        var retained = committed || blocked || readbackFailure ? stored.StoragePath : Assert.Single(Directory.GetFiles(Path.Combine(folder, "_recycle")));
        Assert.Equal("uploaded payload", await File.ReadAllTextAsync(retained));
        Assert.Equal(blocked || readbackFailure, error.Data.Contains("UploadRetentionFailure"));
        if (readbackFailure) Assert.Same(repository.ReadbackFailure, error.Data["UploadRetentionFailure"]);
    }

    [Fact]
    public async Task DocumentRecycleLinkCannotChangeExternalFiles()
    {
        using var scope = new Scope(); using var outside = new Scope(); await scope.SeedAsync();
        var document = await scope.AddDocumentAsync();
        var sentinel = Path.Combine(outside.Root, "sentinel.txt"); await File.WriteAllTextAsync(sentinel, "external payload");
        var link = Path.Combine(Path.GetDirectoryName(document.StoragePath)!, "_recycle");
        using var reviewedLink = RetainedPackageTestFiles.CreateOwnedLink(scope.Root, link, outside.Root, outside.Root);
        await Assert.ThrowsAsync<IOException>(() => scope.Service().DeleteAsync(document.Id, 0));
        Assert.Equal("retained payload", await File.ReadAllTextAsync(document.StoragePath));
        Assert.Equal("external payload", await File.ReadAllTextAsync(sentinel));
        Assert.Single(Directory.GetFileSystemEntries(outside.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeRemovalRetainsAllFilesAndOtherSequences(bool wholeApplication)
    {
        using var scope = new Scope(); await scope.SeedAsync();
        scope.Application.CreateSequence("0001", "original", "other");
        await scope.Applications.UpdateAsync(scope.Application);
        var document = await scope.AddDocumentAsync();
        await scope.Placements.AddAsync(new DocumentPlacement(document.Id, scope.Application.Id, "0000", "m1.2", DocumentPlacementOperation.New, "cover"));
        var other = Path.Combine(scope.Application.WorkingDirectoryPath, "0001", "other.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!); await File.WriteAllTextAsync(other, "other sequence");
        var service = new ApplicationDeletionCoordinator(scope.Applications, scope.Documents, scope.Placements,
            new InMemoryPublishJobRepository(), new WorkspaceDeletionService(scope.Policy), new PassthroughApplicationDeletionTransaction());
        if (wholeApplication) await service.DeleteApplicationAsync(scope.Application, ApplicationDeleteMode.PurgeWorkspace);
        else Assert.True(await service.DeleteSequenceAsync(scope.Application, "0000", ApplicationDeleteMode.PurgeWorkspace));
        Assert.Null(await scope.Documents.GetAsync(document.Id));
        Assert.False(File.Exists(document.StoragePath));
        var recycle = Path.Combine(wholeApplication ? scope.Root : scope.Application.WorkingDirectoryPath, "_recycle");
        var retainedRoot = Assert.Single(Directory.GetDirectories(recycle));
        var payload = Path.Combine(retainedRoot, wholeApplication ? "0000/input.pdf" : "input.pdf");
        Assert.Equal("retained payload", await File.ReadAllTextAsync(payload));
        Assert.Equal("other sequence", await File.ReadAllTextAsync(wholeApplication ? Path.Combine(retainedRoot, "0001/other.pdf") : other));
    }

    [Fact]
    public async Task FailedWorkspaceRecycleDoesNotPartiallyRemoveFiles()
    {
        using var scope = new Scope(); await scope.SeedAsync();
        var document = await scope.AddDocumentAsync();
        await scope.Placements.AddAsync(new DocumentPlacement(document.Id, scope.Application.Id, "0000", "m1.2", DocumentPlacementOperation.New, "cover"));
        await File.WriteAllTextAsync(Path.Combine(scope.Root, "_recycle"), "blocker");
        var service = new ApplicationDeletionCoordinator(scope.Applications, scope.Documents, scope.Placements,
            new InMemoryPublishJobRepository(), new WorkspaceDeletionService(scope.Policy), new PassthroughApplicationDeletionTransaction());
        await Assert.ThrowsAsync<WorkspacePurgeFailedException>(() => service.DeleteApplicationAsync(scope.Application, ApplicationDeleteMode.PurgeWorkspace));
        Assert.Null(await scope.Applications.GetAsync(scope.Application.Id));
        Assert.Equal("retained payload", await File.ReadAllTextAsync(document.StoragePath));
    }

    private sealed class Scope : IDisposable
    {
        public string Root { get; } = RetainedPackageTestFiles.Create();
        public SubmissionApplication Application { get; }
        public InMemoryApplicationRepository Applications { get; } = new();
        public InMemoryDocumentRepository Documents { get; } = new();
        public InMemoryDocumentPlacementRepository Placements { get; } = new();
        public InMemoryWorkspaceRevisionStore Revisions { get; }
        public ConfiguredWorkspacePathPolicy Policy { get; }
        public Scope()
        {
            Application = new("retention-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", Path.Combine(Root, "app"), "us-fda-ectd-3.2.2");
            Application.CreateSequence("0000", "original", "retention");
            Policy = new(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [Root] }));
            Revisions = new(Applications);
        }
        public Task SeedAsync() => Applications.AddAsync(Application);
        public async Task<SubmissionDocument> AddDocumentAsync()
        {
            var path = Path.Combine(Application.WorkingDirectoryPath, "0000", "input.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "retained payload");
            var document = new SubmissionDocument("input.pdf", "application/pdf", 16, "sha", "md5", path);
            await Documents.AddAsync(document); return document;
        }
        public DocumentService Service(IDocumentRepository? repository = null) => new(repository ?? Documents,
            new LocalFileStorage(Options.Create(new FileStorageOptions())), Placements, Applications,
            new ApplicationWorkspaceService(Policy), new EctdWorkspacePathResolver(), new DocumentStorageBoundary(Policy),
            new WorkspaceMutationCoordinator(Revisions, new InMemoryPersistenceTransaction()));
        public void Dispose() => RetainedPackageTestFiles.RetainDirectory(Root);
    }

    private sealed class SyntheticDatabaseException() : System.Data.Common.DbException("Synthetic database readback failure");

    private sealed class FailedRegistration(InMemoryDocumentRepository inner, bool committed, bool readbackFailure) : IDocumentRepository
    {
        public SyntheticDatabaseException ReadbackFailure { get; } = new();
        public IOException Failure { get; } = new("Synthetic registration failure");
        public SubmissionDocument? Attempted { get; private set; }
        public async Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
        {
            Attempted = document;
            // Persist outside the ambient rollback snapshot to simulate a committed
            // registration whose response was lost, then fail the caller.
            if (committed) Stored = document;
            await Task.CompletedTask; throw Failure;
        }
        private SubmissionDocument? Stored { get; set; }
        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => inner.UpdateAsync(document, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => readbackFailure ? Task.FromException<SubmissionDocument?>(ReadbackFailure) : Stored?.Id == id ? Task.FromResult<SubmissionDocument?>(Stored) : inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    }
}
