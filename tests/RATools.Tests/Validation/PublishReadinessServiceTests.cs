using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.Validation;

public sealed class PublishReadinessServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNotReadyWhenValidationHasErrors()
    {
        var applicationId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var service = CreateService(application, [], []);

        var report = await service.GetAsync(new ValidateSequenceRequest(applicationId, "0001"));

        Assert.False(report.IsReady);
        Assert.Equal("Blocked", report.Status);
        Assert.Contains(report.Findings, x => x.Source == "Validation" && x.Code == "NO_PLACEMENTS");
    }

    [Fact]
    public async Task GetAsync_ReturnsNotReadyWhenUsRegionalMetadataWouldBreakRealPublish()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var filePath = CreateTempFile();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var document = SubmissionDocument.Rehydrate(documentId, "cover.pdf", "application/pdf", 10, "sha-cover", filePath, DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            placementId,
            documentId,
            applicationId,
            "0001",
            "m1.2",
            DocumentPlacementOperation.New,
            "Cover Letter",
            null,
            DateTime.UtcNow);
        var service = CreateService(application, [placement], [document]);

        var report = await service.GetAsync(new ValidateSequenceRequest(applicationId, "0001"));

        Assert.False(report.IsReady);
        Assert.Equal("Blocked", report.Status);
        Assert.Contains(report.Findings, x => x.Source == "PublishPreflight" && x.Code == "US_REGIONAL_METADATA_MISSING" && x.FieldName == "ApplicantContactName");
        Assert.DoesNotContain(report.Findings, x => x.Code == "NO_PLACEMENTS");
    }

    [Fact]
    public async Task GetAsync_ReturnsReadyWhenValidationAndPreflightPass()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var filePath = CreateTempFile();
        var metadata = SequencePublishingMetadata.Create(
            "anda",
            "original-application",
            "initial",
            "Initial sequence",
            "Acme Pharma",
            "356h",
            "Jane Regulatory",
            "regulatory",
            "301-555-0100",
            "office",
            "jane.regulatory@example.test");
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow, metadata)],
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);
        var document = SubmissionDocument.Rehydrate(documentId, "cover.pdf", "application/pdf", 10, "sha-cover", filePath, DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            Guid.NewGuid(),
            documentId,
            applicationId,
            "0001",
            "m1.2",
            DocumentPlacementOperation.New,
            "Cover Letter",
            null,
            DateTime.UtcNow);
        var service = CreateService(application, [placement], [document]);

        var report = await service.GetAsync(new ValidateSequenceRequest(applicationId, "0001"));

        Assert.True(report.IsReady);
        Assert.Equal("Ready", report.Status);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.BlockingErrorCount);
        Assert.Equal(0, report.WarningCount);
    }

    private static PublishReadinessService CreateService(
        SubmissionApplication? application,
        IReadOnlyCollection<DocumentPlacement> placements,
        IReadOnlyCollection<SubmissionDocument> documents)
    {
        var applicationRepository = new StubApplicationRepository(application);
        var placementRepository = new StubDocumentPlacementRepository(placements);
        var documentRepository = new StubDocumentRepository(documents);
        var auditLogService = new StubAuditLogService();
        var validationService = new SequenceValidationService(
            applicationRepository,
            placementRepository,
            documentRepository,
            auditLogService,
            new RelaxedValidationProfileProvider());
        var standardsProfileProvider = new FdaEctd322StandardsProfileProvider();
        var packageModelBuilder = new EctdPackageModelBuilder(
            applicationRepository,
            placementRepository,
            documentRepository,
            standardsProfileProvider);

        return new PublishReadinessService(
            validationService,
            packageModelBuilder,
            new IchIndexXmlWriter(),
            new UsRegionalXmlWriter(),
            new EctdXmlValidator());
    }

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"publish-readiness-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "payload");
        return path;
    }

    private sealed class StubApplicationRepository(SubmissionApplication? application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(application is not null && application.Id == id ? application : null);
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

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);
    }

    private sealed class RelaxedValidationProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;
        public ValidationMode Mode => ValidationMode.Relaxed;
    }
}
