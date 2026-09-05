using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;
using RATools.Application.Persistence;
using RATools.Application.Validation;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Storage;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Documents;

public sealed class DocumentMetadataTransactionTests
{
    [Theory]
    [InlineData("renamed")]
    [InlineData("protocol")]
    public async Task UpdateMetadataAsync_RejectsMissingSourceWithoutAdoptingAnotherFile(string fileNamePrefix)
    {
        await using var fixture = await TestFixture.CreateAsync();
        var originalDocument = fixture.Documents.State;
        var originalPlacement = fixture.Placements.State;
        File.Delete(fixture.SourcePath);
        var unrelatedPath = Path.Combine(Path.GetDirectoryName(fixture.SourcePath)!, "unrelated.pdf");
        await File.WriteAllTextAsync(unrelatedPath, "unrelated document");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateMetadataAsync(
            fixture.PlacementId,
            new UpdateDocumentPlacementMetadataRequest("Updated title", "replace", fileNamePrefix, Guid.NewGuid())));

        Assert.Contains("source workspace file", exception.Message);
        Assert.Contains(fixture.SourcePath, exception.Message);
        Assert.Equal(originalDocument, fixture.Documents.State);
        Assert.Equal(originalPlacement, fixture.Placements.State);
        Assert.Empty(fixture.FileStorage.RenameCancellationStates);
        Assert.False(File.Exists(fixture.SourcePath));
        Assert.False(File.Exists(fixture.TargetPath));
        Assert.Equal("unrelated document", await File.ReadAllTextAsync(unrelatedPath));
    }

    [Fact]
    public async Task UpdateMetadataAsync_RestoresDatabaseAndFileWhenPlacementUpdateFails()
    {
        await using var fixture = await TestFixture.CreateAsync(
            placementRepository: new FailOncePlacementRepository());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateMetadataAsync(
            fixture.PlacementId,
            fixture.Request));

        Assert.Contains("could not be updated", exception.Message);
        AssertOldState(fixture);
    }

    [Fact]
    public async Task UpdateMetadataAsync_RestoresDatabaseAndFileWhenCommitOutcomeIsAmbiguous()
    {
        await using var fixture = await TestFixture.CreateAsync(
            persistenceTransaction: new CommitFailureAfterOperationTransaction());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateMetadataAsync(
            fixture.PlacementId,
            fixture.Request));

        Assert.Contains("simulated commit failure", exception.ToString());
        AssertOldState(fixture);
    }

    [Fact]
    public async Task UpdateMetadataAsync_UsesIndependentCleanupTokenAfterRequestCancellation()
    {
        using var requestCts = new CancellationTokenSource();
        var transaction = new CancelAfterOperationTransaction(requestCts);
        await using var fixture = await TestFixture.CreateAsync(persistenceTransaction: transaction);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.UpdateMetadataAsync(
            fixture.PlacementId,
            fixture.Request,
            requestCts.Token));

        AssertOldState(fixture);
        Assert.Equal([false, false], fixture.FileStorage.RenameCancellationStates);
    }

    [Fact]
    public async Task UpdateMetadataAsync_PreservesNewStateWhenFileRollbackFails()
    {
        await using var fixture = await TestFixture.CreateAsync(
            placementRepository: new FailOncePlacementRepository(),
            fileStorage: new FailOnRollbackFileStorage());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateMetadataAsync(
            fixture.PlacementId,
            fixture.Request));

        Assert.Contains("updated metadata was preserved", exception.Message);
        AssertNewState(fixture);
        Assert.True(File.Exists(fixture.TargetPath));
        Assert.False(File.Exists(fixture.SourcePath));
    }

    [Fact]
    public async Task UpdateMetadataAsync_ReportsIncompleteCompensationWhenPlacementIsDeletedConcurrently()
    {
        await using var fixture = await TestFixture.CreateAsync(
            placementRepository: new ConcurrentDeletePlacementRepository());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateMetadataAsync(
            fixture.PlacementId,
            fixture.Request));

        Assert.Contains("compensation was incomplete", exception.Message);
        AssertOldDocumentState(fixture);
        Assert.Null(fixture.Placements.State);
        Assert.True(File.Exists(fixture.SourcePath));
        Assert.False(File.Exists(fixture.TargetPath));
    }

    private static void AssertOldState(TestFixture fixture)
    {
        AssertOldDocumentState(fixture);
        Assert.Equal("Original title", fixture.Placements.State!.Title);
        Assert.Equal(DocumentPlacementOperation.New, fixture.Placements.State!.Operation);
        Assert.True(File.Exists(fixture.SourcePath));
        Assert.False(File.Exists(fixture.TargetPath));
    }

    private static void AssertOldDocumentState(TestFixture fixture)
    {
        Assert.Equal("protocol.pdf", fixture.Documents.State!.FileName);
        Assert.Equal(fixture.SourcePath, fixture.Documents.State.StoragePath);
    }

    private static void AssertNewState(TestFixture fixture)
    {
        Assert.Equal("renamed.pdf", fixture.Documents.State!.FileName);
        Assert.Equal(fixture.TargetPath, fixture.Documents.State.StoragePath);
        Assert.Equal("Updated title", fixture.Placements.State!.Title);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            string rootPath,
            SnapshotDocumentRepository documents,
            SnapshotPlacementRepository placements,
            FaultInjectingFileStorage fileStorage,
            DocumentPlacementService service,
            Guid placementId,
            string sourcePath,
            string targetPath)
        {
            RootPath = rootPath;
            Documents = documents;
            Placements = placements;
            FileStorage = fileStorage;
            Service = service;
            PlacementId = placementId;
            SourcePath = sourcePath;
            TargetPath = targetPath;
        }

        public string RootPath { get; }
        public SnapshotDocumentRepository Documents { get; }
        public SnapshotPlacementRepository Placements { get; }
        public FaultInjectingFileStorage FileStorage { get; }
        public DocumentPlacementService Service { get; }
        public Guid PlacementId { get; }
        public string SourcePath { get; }
        public string TargetPath { get; }
        public UpdateDocumentPlacementMetadataRequest Request { get; } =
            new("Updated title", "new", "renamed", null);

        public static async Task<TestFixture> CreateAsync(
            SnapshotPlacementRepository? placementRepository = null,
            IPersistenceTransaction? persistenceTransaction = null,
            FaultInjectingFileStorage? fileStorage = null)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), $"metadata-transaction-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(rootPath, "0001", "m1", "us", "11-forms");
            Directory.CreateDirectory(sourceDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "protocol.pdf");
            var targetPath = Path.Combine(sourceDirectory, "renamed.pdf");
            await File.WriteAllTextAsync(sourcePath, "payload");

            var applicationId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var placementId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var application = SubmissionApplication.Rehydrate(
                applicationId,
                $"APP-{Guid.NewGuid():N}",
                "US",
                "Sponsor",
                now,
                [SubmissionSequence.Rehydrate("0001", "original", "Original sequence", now)],
                rootPath,
                "us-fda-ectd-3.2.2");
            var document = SubmissionDocument.Rehydrate(
                documentId,
                "protocol.pdf",
                "application/pdf",
                new FileInfo(sourcePath).Length,
                "sha256",
                "md5",
                sourcePath,
                now);
            var placement = DocumentPlacement.Rehydrate(
                placementId,
                documentId,
                applicationId,
                "0001",
                "m1.1",
                DocumentPlacementOperation.New,
                "Original title",
                null,
                now);

            var documents = new SnapshotDocumentRepository(document);
            var placements = placementRepository ?? new SnapshotPlacementRepository(placement);
            if (placementRepository is not null)
            {
                placements.SetState(placement);
            }

            var storage = fileStorage ?? new FaultInjectingFileStorage(
                new LocalFileStorage(Options.Create(new FileStorageOptions())));
            var service = new DocumentPlacementService(
                placements,
                documents,
                storage,
                new StubApplicationRepository(application),
                new StubPublishJobRepository(),
                new StubWorkspacePathResolver(),
                PermissiveDocumentStorageBoundary.Instance,
                persistenceTransaction ?? new PassthroughPersistenceTransaction());

            return new TestFixture(rootPath, documents, placements, storage, service, placementId, sourcePath, targetPath);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record DocumentSnapshot(
        Guid Id,
        string FileName,
        string MediaType,
        long FileSize,
        string Sha256,
        string Md5,
        string StoragePath,
        DateTime CreatedUtc)
    {
        public static DocumentSnapshot From(SubmissionDocument document) => new(
            document.Id,
            document.FileName,
            document.MediaType,
            document.FileSize,
            document.Sha256,
            document.Md5,
            document.StoragePath,
            document.CreatedUtc);

        public SubmissionDocument ToDomain() => SubmissionDocument.Rehydrate(
            Id,
            FileName,
            MediaType,
            FileSize,
            Sha256,
            Md5,
            StoragePath,
            CreatedUtc);
    }

    private sealed class SnapshotDocumentRepository(SubmissionDocument document) : IDocumentRepository
    {
        public DocumentSnapshot State { get; private set; } = DocumentSnapshot.From(document);

        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
        {
            State = DocumentSnapshot.From(document);
            return Task.FromResult(true);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == State.Id ? State.ToDomain() : null);

        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionDocument>>([State.ToDomain()]);
    }

    private class SnapshotPlacementRepository(DocumentPlacement placement) : IDocumentPlacementRepository
    {
        public DocumentPlacementSnapshot? State { get; protected set; } = DocumentPlacementSnapshot.From(placement);
        protected int UpdateCount { get; private set; }

        public void SetState(DocumentPlacement placement) => State = DocumentPlacementSnapshot.From(placement);

        public virtual Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public virtual Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            State = DocumentPlacementSnapshot.From(placement);
            return Task.FromResult(true);
        }

        public virtual Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            State = null;
            return Task.CompletedTask;
        }

        public virtual Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(State is not null && State.Id == id ? State.ToDomain() : null);

        public virtual Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(State is null ? [] : [State.ToDomain()]);

        public virtual Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
            => ListAsync(cancellationToken);

        public virtual Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
            => ListAsync(cancellationToken);
    }

    private sealed class FailOncePlacementRepository : SnapshotPlacementRepository
    {
        private bool _failed;

        public FailOncePlacementRepository()
            : base(DocumentPlacement.Rehydrate(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "0001", "m1.1",
                DocumentPlacementOperation.New, "Original title", null, DateTime.UtcNow))
        {
        }

        public override Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                return Task.FromResult(false);
            }

            return base.UpdateAsync(placement, cancellationToken);
        }
    }

    private sealed class ConcurrentDeletePlacementRepository : SnapshotPlacementRepository
    {
        private bool _deleted;

        public ConcurrentDeletePlacementRepository()
            : base(DocumentPlacement.Rehydrate(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "0001", "m1.1",
                DocumentPlacementOperation.New, "Original title", null, DateTime.UtcNow))
        {
        }

        public override Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
        {
            if (!_deleted)
            {
                _deleted = true;
                State = null;
            }

            return Task.FromResult(false);
        }
    }

    private sealed record DocumentPlacementSnapshot(
        Guid Id,
        Guid DocumentId,
        Guid ApplicationId,
        string SequenceNumber,
        string CtdSection,
        DocumentPlacementOperation Operation,
        string? Title,
        Guid? LifecycleTargetPlacementId,
        DateTime CreatedUtc)
    {
        public static DocumentPlacementSnapshot From(DocumentPlacement placement) => new(
            placement.Id,
            placement.DocumentId,
            placement.ApplicationId,
            placement.SequenceNumber,
            placement.CtdSection,
            placement.Operation,
            placement.Title,
            placement.LifecycleTargetPlacementId,
            placement.CreatedUtc);

        public DocumentPlacement ToDomain() => DocumentPlacement.Rehydrate(
            Id,
            DocumentId,
            ApplicationId,
            SequenceNumber,
            CtdSection,
            Operation,
            Title,
            LifecycleTargetPlacementId,
            CreatedUtc);
    }

    private class FaultInjectingFileStorage(IFileStorage inner) : IFileStorage
    {
        public List<bool> RenameCancellationStates { get; } = [];

        public virtual Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
            => inner.SaveAsync(request, cancellationToken);

        public virtual Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default)
            => inner.MoveAsync(sourcePath, destinationDirectoryPath, cancellationToken);

        public virtual Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
        {
            RenameCancellationStates.Add(cancellationToken.IsCancellationRequested);
            return inner.RenameAsync(sourcePath, targetPath, cancellationToken);
        }
    }

    private sealed class FailOnRollbackFileStorage : FaultInjectingFileStorage
    {
        private int _renameCount;

        public FailOnRollbackFileStorage()
            : base(new LocalFileStorage(Options.Create(new FileStorageOptions())))
        {
        }

        public override Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
        {
            if (++_renameCount == 2)
            {
                throw new IOException("simulated file rollback failure");
            }

            return base.RenameAsync(sourcePath, targetPath, cancellationToken);
        }
    }

    private sealed class CommitFailureAfterOperationTransaction : IPersistenceTransaction
    {
        private bool _failed;

        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await operation(cancellationToken);
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("simulated commit failure");
            }
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var result = await operation(cancellationToken);
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("simulated commit failure");
            }

            return result;
        }
    }

    private sealed class CancelAfterOperationTransaction(CancellationTokenSource requestCts) : IPersistenceTransaction
    {
        private bool _cancelled;

        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await operation(cancellationToken);
            CancelRequestOnce();
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var result = await operation(cancellationToken);
            CancelRequestOnce();
            return result;
        }

        private void CancelRequestOnce()
        {
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            requestCts.Cancel();
            throw new OperationCanceledException(requestCts.Token);
        }
    }

    private sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == application.Id ? application : null);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([application]);
    }

    private sealed class StubPublishJobRepository : IPublishJobRepository
    {
        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateHistorySummaryAsync(Guid jobId, int expectedAttemptCount, PublishJobHistorySummary summary, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PublishJob?>(null);
        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<PublishJob>>([]);
        public Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<PublishJob>>([]);
        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PublishJobHistoryQueryResult([], 0, 0, 0, 0));
    }

    private sealed class StubWorkspacePathResolver : IEctdWorkspacePathResolver
    {
        public EctdWorkspacePathResolution Resolve(string ectdTemplateKey, string ctdSection)
            => new("US", ctdSection, "m1-1-forms", Path.Combine("m1", "us", "11-forms"));
    }
}
