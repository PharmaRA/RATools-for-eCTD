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
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

using RATools.Tests.TestDoubles;

namespace RATools.Tests.Documents;

public sealed class DocumentPlacementServiceTests
{
    [Fact]
    public async Task CreateAsync_RejectsDocumentOwnedByAnotherApplicationWithoutChangingFileOrPlacement()
    {
        var allowedRoot = Path.Combine(Path.GetTempPath(), $"placement-boundary-{Guid.NewGuid():N}");
        var applicationRoot = Path.Combine(allowedRoot, "app-a");
        var otherApplicationSequenceRoot = Path.Combine(allowedRoot, "app-b", "0001");
        Directory.CreateDirectory(applicationRoot);
        Directory.CreateDirectory(otherApplicationSequenceRoot);
        var outsidePath = Path.Combine(otherApplicationSequenceRoot, "outside.pdf");
        await File.WriteAllTextAsync(outsidePath, "must remain unchanged");

        try
        {
            var applicationId = Guid.NewGuid();
            var application = SubmissionApplication.Rehydrate(
                applicationId,
                "APP-A",
                "US",
                "Sponsor",
                DateTime.UtcNow,
                [SubmissionSequence.Rehydrate("0001", "original", "Original", DateTime.UtcNow)],
                applicationRoot,
                "us-fda-ectd-3.2.2");
            var document = SubmissionDocument.Rehydrate(
                Guid.NewGuid(),
                "outside.pdf",
                "application/pdf",
                new FileInfo(outsidePath).Length,
                "sha256",
                "md5",
                outsidePath,
                DateTime.UtcNow);
            var placementRepository = new StubPlacementRepository();
            var boundary = CreateBoundary(allowedRoot);
            var service = new DocumentPlacementService(
                placementRepository,
                new StubDocumentRepository(document),
                new StubFileStorage(),
                new StubApplicationRepository(application),
                new StubPublishJobRepository(),
                new StubWorkspacePathResolver(),
                boundary,
                new PassthroughPersistenceTransaction());

            await Assert.ThrowsAsync<DocumentStorageBoundaryException>(() => service.CreateAsync(
                new CreateDocumentPlacementRequest(
                    document.Id,
                    applicationId,
                    "0001",
                    "m1.1",
                    "new",
                    "Outside")));

            Assert.False(placementRepository.AddCalled);
            Assert.Equal("must remain unchanged", await File.ReadAllTextAsync(outsidePath));
        }
        finally
        {
            Directory.Delete(allowedRoot, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsync_RejectsNumericOperationValues()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"placement-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var applicationId = Guid.NewGuid();
            var sequenceNumber = "0001";
            var sequenceDirectory = Path.Combine(tempDirectory, sequenceNumber, "m1", "us", "11-forms");
            Directory.CreateDirectory(sequenceDirectory);
            var sourcePath = Path.Combine(sequenceDirectory, "protocol.pdf");
            await File.WriteAllTextAsync(sourcePath, "payload");

            var application = SubmissionApplication.Rehydrate(
                applicationId,
                "APP-001",
                "US",
                "Sponsor",
                DateTime.UtcNow,
                [SubmissionSequence.Rehydrate(sequenceNumber, "Original", "Original sequence", DateTime.UtcNow)],
                tempDirectory,
                "us-fda-ectd-3.2.2");
            var document = SubmissionDocument.Rehydrate(
                Guid.NewGuid(),
                "protocol.pdf",
                "application/pdf",
                new FileInfo(sourcePath).Length,
                "sha256",
                "md5",
                sourcePath,
                DateTime.UtcNow);
            var placement = DocumentPlacement.Rehydrate(
                Guid.NewGuid(),
                document.Id,
                applicationId,
                sequenceNumber,
                "m1.1",
                DocumentPlacementOperation.New,
                "Protocol",
                null,
                DateTime.UtcNow);
            var service = new DocumentPlacementService(
                new StubPlacementRepository(placement),
                new StubDocumentRepository(document),
                new StubFileStorage(),
                new StubApplicationRepository(application),
                new StubPublishJobRepository(),
                new StubWorkspacePathResolver(),
                PermissiveDocumentStorageBoundary.Instance,
                new PassthroughPersistenceTransaction());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateMetadataAsync(
                placement.Id,
                new UpdateDocumentPlacementMetadataRequest("Protocol", "999", "protocol", null)));

            Assert.Contains("Unsupported placement operation", exception.Message);
            Assert.Equal(DocumentPlacementOperation.New, placement.Operation);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static DocumentStorageBoundary CreateBoundary(string allowedRoot)
        => new(new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = [allowedRoot]
        })));

    private sealed class StubPlacementRepository(DocumentPlacement? placement = null) : IDocumentPlacementRepository
    {
        public bool AddCalled { get; private set; }

        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
        {
            AddCalled = true;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(placement is not null && id == placement.Id ? placement : null);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placement is null ? [] : [placement]);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placement is null ? [] : [placement]);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placement is null ? [] : [placement]);
    }

    private sealed class StubDocumentRepository(SubmissionDocument document) : IDocumentRepository
    {
        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == document.Id ? document : null);

        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionDocument>>([document]);
    }

    private sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == application.Id ? application : null);

        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([application]);
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileUploadResult(request.FileName, "application/octet-stream", 0, string.Empty, string.Empty, request.FileName));

        public Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default) => Task.FromResult(sourcePath);

        public Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) => Task.FromResult(targetPath);
    }

    private sealed class StubPublishJobRepository : IPublishJobRepository
    {
        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateHistorySummaryAsync(
            Guid jobId,
            int expectedAttemptCount,
            PublishJobHistorySummary summary,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

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
