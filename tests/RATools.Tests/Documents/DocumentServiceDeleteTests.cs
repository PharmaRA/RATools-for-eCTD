using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;
using RATools.Application.Validation;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.Documents;

public sealed class DocumentServiceDeleteTests
{
    [Fact]
    public async Task DeleteAsync_StopsCheckingSharedStoragePathAfterFirstMatch()
    {
        var sharedPath = Path.Combine(Path.GetTempPath(), $"ratools-shared-{Guid.NewGuid():N}.pdf");
        var document = Document(Guid.NewGuid(), sharedPath);
        var sharedDocument = Document(Guid.NewGuid(), sharedPath);
        var repository = new DeleteDocumentRepository(
            document,
            new ThrowAfterSharedPathMatchDocuments(document, sharedDocument));
        var service = CreateService(repository);

        var deleted = await service.DeleteAsync(document.Id);

        Assert.True(deleted);
        Assert.Equal(document.Id, repository.DeletedId);
    }

    private static DocumentService CreateService(IDocumentRepository documentRepository)
        => new(
            documentRepository,
            new StubFileStorage(),
            new EmptyPlacementRepository(),
            new EmptyApplicationRepository(),
            new StubWorkspaceService(),
            new StubWorkspacePathResolver(),
            new AllowingWorkspacePathPolicy());

    private static SubmissionDocument Document(Guid id, string storagePath)
        => SubmissionDocument.Rehydrate(
            id,
            Path.GetFileName(storagePath),
            "application/pdf",
            1,
            $"sha-{id:N}",
            $"md5-{id:N}",
            storagePath,
            DateTime.UtcNow);

    private sealed class DeleteDocumentRepository(
        SubmissionDocument document,
        IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository
    {
        public Guid? DeletedId { get; private set; }

        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            DeletedId = id;
            return Task.CompletedTask;
        }

        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == document.Id ? document : null);

        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(documents);
    }

    private sealed class ThrowAfterSharedPathMatchDocuments(
        SubmissionDocument document,
        SubmissionDocument sharedDocument) : IReadOnlyCollection<SubmissionDocument>
    {
        public int Count => 3;

        public IEnumerator<SubmissionDocument> GetEnumerator()
        {
            yield return document;
            yield return sharedDocument;
            throw new InvalidOperationException("Shared storage path lookup should stop after the first match.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class EmptyPlacementRepository : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DocumentPlacement?>(null);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
    }

    private sealed class EmptyApplicationRepository : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<SubmissionApplication?>(null);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([]);
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileUploadResult(request.FileName, request.MediaType, 0, string.Empty, string.Empty, request.FileName));

        public Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult(sourcePath);
    }

    private sealed class StubWorkspaceService : IApplicationWorkspaceService
    {
        public Task<string> EnsureApplicationWorkingDirectoryAsync(string parentPath, string applicationNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.Combine(parentPath, applicationNumber));

        public Task<string> EnsureSequenceWorkingDirectoryAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.Combine(applicationWorkingDirectoryPath, sequenceNumber));
    }

    private sealed class StubWorkspacePathResolver : IEctdWorkspacePathResolver
    {
        public EctdWorkspacePathResolution Resolve(string ectdTemplateKey, string ctdSection)
            => new("US", ctdSection, "m1-1", Path.Combine("m1", "us", "11-forms"));
    }

    private sealed class AllowingWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path) => path;
    }
}
