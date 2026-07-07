using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.Publishing;

namespace RATools.Tests.Documents;

public sealed class DocumentPlacementServiceTests
{
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
                new StubWorkspacePathResolver());

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

    private sealed class StubPlacementRepository(DocumentPlacement placement) : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == placement.Id ? placement : null);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([placement]);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([placement]);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([placement]);
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
    }

    private sealed class StubPublishJobRepository : IPublishJobRepository
    {
        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PublishJob?>(null);

        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<PublishJob>>([]);

        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PublishJobHistoryQueryResult([], 0, 0, 0, 0));
    }

    private sealed class StubWorkspacePathResolver : IEctdWorkspacePathResolver
    {
        public EctdWorkspacePathResolution Resolve(string ectdTemplateKey, string ctdSection)
            => new("US", ctdSection, "m1-1-forms", Path.Combine("m1", "us", "11-forms"));
    }
}
