using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.Validation;

public sealed class SequenceValidationLifecycleTargetTests
{
    [Fact]
    public async Task ValidateAsync_MatchesExplicitHistoricalLifecycleTarget()
    {
        var applicationId = Guid.NewGuid();
        var historicalDocumentId = Guid.NewGuid();
        var currentDocumentId = Guid.NewGuid();
        var historicalPlacementId = Guid.NewGuid();
        var currentPlacementId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [
                SubmissionSequence.Rehydrate("0000", "original", "Original", DateTime.UtcNow),
                SubmissionSequence.Rehydrate("0001", "supplement", "Supplement", DateTime.UtcNow)
            ],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var documents = new[]
        {
            SubmissionDocument.Rehydrate(historicalDocumentId, "historical.pdf", "application/pdf", 1, "sha", "md5", CreateTempFile(), DateTime.UtcNow),
            SubmissionDocument.Rehydrate(currentDocumentId, "current.pdf", "application/pdf", 1, "sha", "md5", CreateTempFile(), DateTime.UtcNow)
        };
        var historicalPlacement = DocumentPlacement.Rehydrate(
            historicalPlacementId,
            historicalDocumentId,
            applicationId,
            "0000",
            "m1.1",
            DocumentPlacementOperation.New,
            "Historical Leaf",
            null,
            DateTime.UtcNow);
        var currentPlacement = DocumentPlacement.Rehydrate(
            currentPlacementId,
            currentDocumentId,
            applicationId,
            "0001",
            "m1.1",
            DocumentPlacementOperation.Replace,
            "Current Leaf",
            historicalPlacementId,
            DateTime.UtcNow);
        var service = new SequenceValidationService(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository([historicalPlacement, currentPlacement]),
            new StubDocumentRepository(documents),
            new StubAuditLogService(),
            new StubValidationProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0001"));

        var match = Assert.Single(report.LifecycleMatches);
        Assert.Equal("MATCHED", match.ResultCode);
        Assert.Equal("ExplicitPlacementId", match.MatchStrategy);
        Assert.Contains(historicalPlacementId, match.HistoricalPlacementIds);
    }

    [Fact]
    public async Task ValidateAsync_AddsLocatorFieldsToPlacementIssues()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original", "Original", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var missingPath = Path.Combine(Path.GetTempPath(), $"validation-missing-{Guid.NewGuid():N}.pdf");
        var document = SubmissionDocument.Rehydrate(documentId, "current.pdf", "application/pdf", 1, "sha", "md5", missingPath, DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            placementId,
            documentId,
            applicationId,
            "0000",
            "m1.1",
            DocumentPlacementOperation.New,
            "Current Leaf",
            null,
            DateTime.UtcNow);
        var service = new SequenceValidationService(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository([placement]),
            new StubDocumentRepository([document]),
            new StubAuditLogService(),
            new StubValidationProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0000"));

        var issue = Assert.Single(report.Issues, x => x.Code == "FILE_MISSING");
        Assert.Equal("m1.1", issue.SectionPath);
        Assert.Equal(documentId, issue.DocumentId);
        Assert.Equal(placementId, issue.PlacementId);
    }

    [Fact]
    public async Task ValidateAsync_AddsLocatorFieldsToLifecycleIssues()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "supplement", "Supplement", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var document = SubmissionDocument.Rehydrate(documentId, "current.pdf", "application/pdf", 1, "sha", "md5", CreateTempFile(), DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            placementId,
            documentId,
            applicationId,
            "0001",
            "m1.1",
            DocumentPlacementOperation.Replace,
            "Current Leaf",
            null,
            DateTime.UtcNow);
        var service = new SequenceValidationService(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository([placement]),
            new StubDocumentRepository([document]),
            new StubAuditLogService(),
            new StubValidationProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0001"));

        var lifecycleMatch = Assert.Single(report.LifecycleMatches);
        var issue = Assert.Single(report.Issues, x => x.Code == lifecycleMatch.ResultCode);
        Assert.NotEqual("FILE_MISSING", issue.Code);
        Assert.Equal("m1.1", issue.SectionPath);
        Assert.Equal(documentId, issue.DocumentId);
        Assert.Equal(placementId, issue.PlacementId);
    }

    [Fact]
    public async Task ValidateAsync_LeavesApplicationLevelIssuesWithoutLocatorFields()
    {
        var applicationId = Guid.NewGuid();
        var service = new SequenceValidationService(
            new StubApplicationRepository(null),
            new StubDocumentPlacementRepository([]),
            new StubDocumentRepository([]),
            new StubAuditLogService(),
            new StubValidationProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0000"));

        var issue = Assert.Single(report.Issues, x => x.Code == "APP_NOT_FOUND");
        Assert.Null(issue.SectionPath);
        Assert.Null(issue.DocumentId);
        Assert.Null(issue.PlacementId);
    }

    [Fact]
    public async Task ValidateAsync_AddsLocatorFieldsToDuplicatePlacementsWithoutExpandingIssueCount()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var firstPlacementId = Guid.NewGuid();
        var secondPlacementId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original", "Original", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var document = SubmissionDocument.Rehydrate(documentId, "duplicate.pdf", "application/pdf", 1, "sha", "md5", CreateTempFile(), DateTime.UtcNow);
        var firstPlacement = DocumentPlacement.Rehydrate(
            firstPlacementId,
            documentId,
            applicationId,
            "0000",
            "m1.1",
            DocumentPlacementOperation.New,
            "Duplicate Leaf 1",
            null,
            DateTime.UtcNow);
        var secondPlacement = DocumentPlacement.Rehydrate(
            secondPlacementId,
            documentId,
            applicationId,
            "0000",
            "m1.1",
            DocumentPlacementOperation.New,
            "Duplicate Leaf 2",
            null,
            DateTime.UtcNow);
        var service = new SequenceValidationService(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository([firstPlacement, secondPlacement]),
            new StubDocumentRepository([document]),
            new StubAuditLogService(),
            new StrictValidationProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0000"));

        var issue = Assert.Single(report.Issues, x => x.Code == "DUPLICATE_PLACEMENT");
        Assert.Equal("m1.1", issue.SectionPath);
        Assert.Equal(documentId, issue.DocumentId);
        Assert.Equal(firstPlacementId, issue.PlacementId);
    }

    [Fact]
    public async Task ValidateAsync_AddsLocatorFieldsToDuplicatePublishedPathWithoutExpandingIssueCount()
    {
        var applicationId = Guid.NewGuid();
        var firstDocumentId = Guid.NewGuid();
        var secondDocumentId = Guid.NewGuid();
        var firstPlacementId = Guid.NewGuid();
        var secondPlacementId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original", "Original", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var sharedStoragePath = CreateTempFile();
        var firstDocument = SubmissionDocument.Rehydrate(firstDocumentId, "shared.pdf", "application/pdf", 1, "sha", "md5", sharedStoragePath, DateTime.UtcNow);
        var secondDocument = SubmissionDocument.Rehydrate(secondDocumentId, "shared.pdf", "application/pdf", 1, "sha", "md5", sharedStoragePath, DateTime.UtcNow);
        var firstPlacement = DocumentPlacement.Rehydrate(
            firstPlacementId,
            firstDocumentId,
            applicationId,
            "0000",
            "m1.1",
            DocumentPlacementOperation.New,
            "Shared Leaf 1",
            null,
            DateTime.UtcNow);
        var secondPlacement = DocumentPlacement.Rehydrate(
            secondPlacementId,
            secondDocumentId,
            applicationId,
            "0000",
            "m1.1",
            DocumentPlacementOperation.New,
            "Shared Leaf 2",
            null,
            DateTime.UtcNow);
        var service = new SequenceValidationService(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository([firstPlacement, secondPlacement]),
            new StubDocumentRepository([firstDocument, secondDocument]),
            new StubAuditLogService(),
            new StubValidationProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0000"));

        var issue = Assert.Single(report.Issues, x => x.Code == "DUPLICATE_PUBLISHED_DOCUMENT_PATH");
        Assert.Equal("m1.1", issue.SectionPath);
        Assert.Equal(firstDocumentId, issue.DocumentId);
        Assert.Equal(firstPlacementId, issue.PlacementId);
    }

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"validation-lifecycle-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "payload");
        return path;
    }

    private sealed class StubApplicationRepository(SubmissionApplication? application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(application is not null && id == application.Id ? application : null);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>(application is null ? [] : [application]);
    }

    private sealed class StubDocumentPlacementRepository(IReadOnlyCollection<DocumentPlacement> placements) : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(placements.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(placements);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId).ToArray());
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber).ToArray());
    }

    private sealed class StubDocumentRepository(IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository
    {
        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(documents.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(documents);
    }

    private sealed class StubAuditLogService : IAuditLogService
    {
        public Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, request.Actor, request.Details, DateTime.UtcNow));
        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);
    }

    private sealed class StubValidationProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;
        public ValidationMode Mode => ValidationMode.Relaxed;
    }

    private sealed class StrictValidationProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;
        public ValidationMode Mode => ValidationMode.Strict;
    }
}
