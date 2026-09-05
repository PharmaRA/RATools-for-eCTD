using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Storage;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Documents;

public sealed class DocumentSectionTransactionTests
{
    [Theory]
    [InlineData(FailureStage.AfterFileMove)]
    [InlineData(FailureStage.AfterDocumentSave)]
    [InlineData(FailureStage.AfterPlacementSave)]
    [InlineData(FailureStage.AfterCommit)]
    public async Task UpdateSectionAsync_RestoresFileAndDatabaseAfterCancellation(FailureStage stage)
    {
        await using var fixture = await Fixture.CreateAsync(stage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.MoveAsync());

        await AssertStateAsync(fixture, moved: false);
        Assert.False(Assert.Single(fixture.Storage.RestoreCancellationStates));
    }

    [Fact]
    public async Task UpdateSectionAsync_RestoresStateWhenPlacementUpdateIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync(FailureStage.RejectPlacementUpdate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.MoveAsync());

        await AssertStateAsync(fixture, moved: false);
    }

    [Fact]
    public async Task UpdateSectionAsync_RestoresSectionWhenFolderDoesNotChange()
    {
        await using var fixture = await Fixture.CreateAsync(FailureStage.AfterPlacementSave, sameFolder: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.MoveAsync());

        await AssertStateAsync(fixture, moved: false);
        Assert.Null(fixture.Storage.MovedPath);
    }

    [Fact]
    public async Task UpdateSectionAsync_PreservesMovedStateWhenFileCannotBeRestored()
    {
        await using var fixture = await Fixture.CreateAsync(FailureStage.AfterPlacementSave, failRestore: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.MoveAsync());

        Assert.Contains("updated section was preserved", exception.Message);
        await AssertStateAsync(fixture, moved: true);
        var causes = Assert.IsType<AggregateException>(exception.InnerException).Flatten().InnerExceptions;
        Assert.Contains(causes, cause => cause is OperationCanceledException);
        Assert.Contains(causes, cause => cause is IOException);
    }

    [Fact]
    public async Task UpdateSectionAsync_CommitsNewPathAndSectionTogether()
    {
        await using var fixture = await Fixture.CreateAsync(FailureStage.None);

        var result = await fixture.MoveAsync();

        Assert.Equal("m1.3", result!.CtdSection);
        await AssertStateAsync(fixture, moved: true);
        Assert.Empty(fixture.Storage.RestoreCancellationStates);
    }

    private static async Task AssertStateAsync(Fixture fixture, bool moved)
    {
        fixture.Database.ChangeTracker.Clear();
        var document = await fixture.Database.Documents.SingleAsync();
        var placement = await fixture.Database.DocumentPlacements.SingleAsync();
        var expectedPath = moved ? fixture.Storage.MovedPath! : fixture.OriginalPath;
        Assert.Equal(expectedPath, document.StoragePath);
        Assert.Equal(moved ? "m1.3" : "m1.2", placement.CtdSection);
        Assert.Equal("move.txt", document.FileName);
        Assert.Equal("move payload", await File.ReadAllTextAsync(expectedPath));
        Assert.Single(Directory.EnumerateFiles(fixture.Workspace.RootPath, "*.txt", SearchOption.AllDirectories));
    }

    public enum FailureStage { None, AfterFileMove, AfterDocumentSave, AfterPlacementSave, AfterCommit, RejectPlacementUpdate }

    private sealed class FailurePlan(FailureStage stage) : IDisposable
    {
        private bool triggered;
        public CancellationTokenSource RequestCancellation { get; } = new();

        public bool Take(FailureStage current)
        {
            if (triggered || current != stage)
            {
                return false;
            }
            triggered = true;
            return true;
        }

        public void CancelAt(FailureStage current, CancellationToken token, bool throwAfterCancel = true)
        {
            if (Take(current))
            {
                RequestCancellation.Cancel();
                if (throwAfterCancel)
                {
                    token.ThrowIfCancellationRequested();
                }
            }
        }

        public void Dispose() => RequestCancellation.Dispose();
    }

    private sealed class Fixture(
        EctdWorkspaceFixture workspace, SqliteConnection connection, RAToolsDbContext database,
        FailurePlan failure, RecordingFileStorage storage, DocumentPlacementService service, Guid placementId, string originalPath) : IAsyncDisposable
    {
        public EctdWorkspaceFixture Workspace { get; } = workspace;
        public RAToolsDbContext Database { get; } = database;
        public RecordingFileStorage Storage { get; } = storage;
        public string OriginalPath { get; } = originalPath;

        public Task<RATools.Application.Documents.Dtos.DocumentPlacementDto?> MoveAsync()
            => service.UpdateSectionAsync(placementId, new UpdateDocumentPlacementSectionRequest("m1.3"), failure.RequestCancellation.Token);

        public static async Task<Fixture> CreateAsync(FailureStage stage, bool failRestore = false, bool sameFolder = false)
        {
            var workspace = new EctdWorkspaceFixture("us-fda-ectd-3.2.2");
            await workspace.AddSequenceAsync("0000");
            var source = await workspace.AddDocumentAsync("0000", "m1.2", "move.txt", "move payload");
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>().UseSqlite(connection).Options);
            await database.Database.EnsureCreatedAsync();
            var applications = new EfCoreApplicationRepository(database);
            var documents = new EfCoreDocumentRepository(database);
            var placements = new EfCoreDocumentPlacementRepository(database);
            await applications.AddAsync(workspace.Application);
            await documents.AddAsync(source.Document);
            await placements.AddAsync(source.Placement);
            database.ChangeTracker.Clear();
            var failure = new FailurePlan(stage);
            var storage = new RecordingFileStorage(new LocalFileStorage(Options.Create(new FileStorageOptions())), failure, failRestore);
            IEctdWorkspacePathResolver resolver = sameFolder ? new SameFolderResolver() : new EctdWorkspacePathResolver();
            var service = new DocumentPlacementService(new PlacementRepository(placements, failure),
                new DocumentRepository(documents, failure), storage, applications, new InMemoryPublishJobRepository(), resolver,
                new DocumentStorageBoundary(workspace.PathPolicy), new Transaction(new EfCorePersistenceTransaction(database), failure));
            return new Fixture(workspace, connection, database, failure, storage, service, source.Placement.Id, source.Document.StoragePath);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
            failure.Dispose();
            Workspace.Dispose();
        }
    }

    private sealed class RecordingFileStorage(IFileStorage inner, FailurePlan failure, bool failRestore) : IFileStorage
    {
        public string? MovedPath { get; private set; }
        public List<bool> RestoreCancellationStates { get; } = [];
        public Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
            => inner.SaveAsync(request, cancellationToken);
        public async Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default)
        {
            var path = await inner.MoveAsync(sourcePath, destinationDirectoryPath, cancellationToken);
            MovedPath ??= path;
            failure.CancelAt(FailureStage.AfterFileMove, cancellationToken, throwAfterCancel: false);
            return path;
        }
        public Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
        {
            RestoreCancellationStates.Add(cancellationToken.IsCancellationRequested);
            if (failRestore)
            {
                throw new IOException("Simulated file restoration failure.");
            }
            return inner.RenameAsync(sourcePath, targetPath, cancellationToken);
        }
    }

    private sealed class DocumentRepository(IDocumentRepository inner, FailurePlan failure) : IDocumentRepository
    {
        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => inner.AddAsync(document, cancellationToken);
        public async Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
        {
            var result = await inner.UpdateAsync(document, cancellationToken);
            failure.CancelAt(FailureStage.AfterDocumentSave, cancellationToken);
            return result;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    }

    private sealed class PlacementRepository(IDocumentPlacementRepository inner, FailurePlan failure) : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => inner.AddAsync(placement, cancellationToken);
        public async Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
        {
            if (failure.Take(FailureStage.RejectPlacementUpdate))
            {
                return false;
            }
            var result = await inner.UpdateAsync(placement, cancellationToken);
            failure.CancelAt(FailureStage.AfterPlacementSave, cancellationToken);
            return result;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => inner.ListByApplicationAsync(applicationId, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => inner.ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken);
    }

    private sealed class Transaction(IPersistenceTransaction inner, FailurePlan failure) : IPersistenceTransaction
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await inner.ExecuteAsync(operation, cancellationToken);
            failure.CancelAt(FailureStage.AfterCommit, cancellationToken);
        }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var result = await inner.ExecuteAsync(operation, cancellationToken);
            failure.CancelAt(FailureStage.AfterCommit, cancellationToken);
            return result;
        }
    }

    private sealed class SameFolderResolver : IEctdWorkspacePathResolver
    {
        public EctdWorkspacePathResolution Resolve(string ectdTemplateKey, string ctdSection)
            => new EctdWorkspacePathResolver().Resolve(ectdTemplateKey, "m1.2");
    }
}
