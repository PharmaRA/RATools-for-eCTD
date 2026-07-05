using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;
using RATools.Application.Validation;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.Documents;

public sealed class DocumentServiceQueryTests
{
    [Fact]
    public async Task ListByApplicationAsync_LoadsOnlyDocumentsReferencedByApplicationPlacements()
    {
        var applicationId = Guid.NewGuid();
        var otherApplicationId = Guid.NewGuid();
        var documentInFirstSequence = Document(Guid.NewGuid(), "first.pdf");
        var documentInSecondSequence = Document(Guid.NewGuid(), "second.pdf");
        var documentInOtherApplication = Document(Guid.NewGuid(), "other.pdf");
        var documentRepository = new RecordingDocumentLookupRepository([
            documentInFirstSequence,
            documentInSecondSequence,
            documentInOtherApplication
        ]);
        var placementRepository = new StubPlacementRepository([
            Placement(applicationId, "0001", documentInFirstSequence.Id),
            Placement(applicationId, "0002", documentInSecondSequence.Id),
            Placement(otherApplicationId, "0001", documentInOtherApplication.Id)
        ]);
        var service = CreateService(documentRepository, placementRepository);

        var result = await service.ListByApplicationAsync(applicationId, sequenceNumber: null);

        Assert.Equal([documentInFirstSequence.Id, documentInSecondSequence.Id], result.Select(x => x.Id));
        Assert.Equal([documentInFirstSequence.Id, documentInSecondSequence.Id], documentRepository.LastRequestedIds);
        Assert.False(documentRepository.ListAllCalled);
    }

    [Fact]
    public async Task ListByApplicationAsync_CanLimitDocumentsToSequencePlacements()
    {
        var applicationId = Guid.NewGuid();
        var documentInFirstSequence = Document(Guid.NewGuid(), "first.pdf");
        var documentInSecondSequence = Document(Guid.NewGuid(), "second.pdf");
        var documentRepository = new RecordingDocumentLookupRepository([
            documentInFirstSequence,
            documentInSecondSequence
        ]);
        var placementRepository = new StubPlacementRepository([
            Placement(applicationId, "0001", documentInFirstSequence.Id),
            Placement(applicationId, "0002", documentInSecondSequence.Id)
        ]);
        var service = CreateService(documentRepository, placementRepository);

        var result = await service.ListByApplicationAsync(applicationId, "0001");

        Assert.Equal([documentInFirstSequence.Id], result.Select(x => x.Id));
        Assert.Equal([documentInFirstSequence.Id], documentRepository.LastRequestedIds);
        Assert.False(documentRepository.ListAllCalled);
    }

    private static DocumentService CreateService(IDocumentRepository documentRepository, IDocumentPlacementRepository placementRepository)
        => new(
            documentRepository,
            new StubFileStorage(),
            placementRepository,
            new StubApplicationRepository(),
            new StubWorkspaceService(),
            new StubWorkspacePathResolver(),
            new AllowingWorkspacePathPolicy());

    private static SubmissionDocument Document(Guid id, string fileName)
        => SubmissionDocument.Rehydrate(
            id,
            fileName,
            "application/pdf",
            1,
            $"sha-{id:N}",
            $"md5-{id:N}",
            Path.Combine(Path.GetTempPath(), fileName),
            DateTime.UtcNow);

    private static DocumentPlacement Placement(Guid applicationId, string sequenceNumber, Guid documentId)
        => DocumentPlacement.Rehydrate(
            Guid.NewGuid(),
            documentId,
            applicationId,
            sequenceNumber,
            "m1.1",
            DocumentPlacementOperation.New,
            null,
            null,
            DateTime.UtcNow);

    private sealed class RecordingDocumentLookupRepository(IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository, IDocumentLookupRepository
    {
        public IReadOnlyCollection<Guid> LastRequestedIds { get; private set; } = [];

        public bool ListAllCalled { get; private set; }

        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(documents.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListAllCalled = true;
            return Task.FromResult(documents);
        }

        public Task<IReadOnlyCollection<SubmissionDocument>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            LastRequestedIds = ids.ToArray();
            var idSet = ids.ToHashSet();
            return Task.FromResult<IReadOnlyCollection<SubmissionDocument>>(documents.Where(x => idSet.Contains(x.Id)).ToArray());
        }
    }

    private sealed class StubPlacementRepository(IReadOnlyCollection<DocumentPlacement> placements) : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(placements.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(placements);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId).ToArray());

        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements
                .Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber)
                .ToArray());
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileUploadResult(request.FileName, request.MediaType, 0, string.Empty, string.Empty, request.FileName));

        public Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult(sourcePath);
    }

    private sealed class StubApplicationRepository : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<SubmissionApplication?>(null);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([]);
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
