using RATools.Application.Validation.Rules;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules.Pdf;
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
        Assert.Contains(report.CategorySummaries, x =>
            x.Category == "SequenceContent"
            && x.BlockingErrorCount == 1
            && x.WarningCount == 0
            && x.FindingCount == 1);
        Assert.Contains(report.Findings, x =>
            x.Source == "Validation"
            && x.Code == "NO_PLACEMENTS"
            && x.Category == "SequenceContent"
            && x.RecommendedAction == "Add at least one document placement to the sequence before publishing.");
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
        var document = SubmissionDocument.Rehydrate(documentId, "cover.pdf", "application/pdf", 10, "sha-cover", "md5-cover", filePath, DateTime.UtcNow);
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
        Assert.Contains(report.CategorySummaries, x =>
            x.Category == "RegionalMetadata"
            && x.BlockingErrorCount == 1
            && x.WarningCount == 0
            && x.FindingCount == 1);
        Assert.Equal(["ApplicantContactName"], report.MissingMetadataFields);
        Assert.Contains(report.Findings, x =>
            x.Source == "PublishPreflight"
            && x.Code == "US_REGIONAL_METADATA_MISSING"
            && x.FieldName == "ApplicantContactName"
            && x.Category == "RegionalMetadata"
            && x.RecommendedAction == "Populate the required US Regional publishing metadata field before publishing.");
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
        var document = SubmissionDocument.Rehydrate(documentId, "cover.pdf", "application/pdf", 10, "sha-cover", "md5-cover", filePath, DateTime.UtcNow);
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
        Assert.Empty(report.CategorySummaries);
        Assert.Empty(report.MissingMetadataFields);
        Assert.Equal(0, report.BlockingErrorCount);
        Assert.Equal(0, report.WarningCount);
    }

    [Fact]
    public async Task GetAsync_ReturnsNotReadyWhenValidationCriteriaRuleFindsInvalidFileName()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
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
        var document = SubmissionDocument.Rehydrate(documentId, "Study Report.PDF", "application/pdf", 10, "sha-study", "md5-study", filePath, DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            placementId,
            documentId,
            applicationId,
            "0001",
            "m1.2",
            DocumentPlacementOperation.New,
            "Study Report",
            null,
            DateTime.UtcNow);
        var service = CreateService(application, [placement], [document]);

        var report = await service.GetAsync(new ValidateSequenceRequest(applicationId, "0001"));

        Assert.False(report.IsReady);
        Assert.Equal("Blocked", report.Status);
        Assert.Equal(1, report.BlockingErrorCount);
        Assert.Empty(report.MissingMetadataFields);
        var finding = Assert.Single(report.Findings, x => x.Code == "FDA-NAMING-1");
        Assert.Equal("ValidationCriteria", finding.Source);
        Assert.Equal("Error", finding.Severity);
        Assert.Equal("FileNaming", finding.Category);
        Assert.Contains("Study Report.PDF", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Rename the file", finding.RecommendedAction, StringComparison.Ordinal);
        Assert.Contains(report.CategorySummaries, x =>
            x.Category == "FileNaming"
            && x.BlockingErrorCount == 1
            && x.WarningCount == 0
            && x.FindingCount == 1);
    }

    [Fact]
    public async Task GetAsync_ReturnsNotReadyWhenPdfComplianceRuleFindsEncryptedPdf()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
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
        var document = SubmissionDocument.Rehydrate(documentId, "encrypted.pdf", "application/pdf", 10, "sha-study", "md5-study", filePath, DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            placementId,
            documentId,
            applicationId,
            "0001",
            "m1.2",
            DocumentPlacementOperation.New,
            "Encrypted PDF",
            null,
            DateTime.UtcNow);
        var pdfRule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: true,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [])));
        var service = CreateService(application, [placement], [document], [new FileNamingConventionRule(), pdfRule]);

        var report = await service.GetAsync(new ValidateSequenceRequest(applicationId, "0001"));

        Assert.False(report.IsReady);
        var finding = Assert.Single(report.Findings, x => x.Code == "PDF_ENCRYPTED");
        Assert.Equal("ValidationCriteria", finding.Source);
        Assert.Equal("Error", finding.Severity);
        Assert.Equal("PdfCompliance", finding.Category);
        Assert.Contains(report.CategorySummaries, x =>
            x.Category == "PdfCompliance"
            && x.BlockingErrorCount == 1
            && x.WarningCount == 0
            && x.FindingCount == 1);
    }

    [Fact]
    public async Task GetAsync_ReturnsReadyForEuTemplateWithoutUsRegionalMetadata()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var filePath = CreateTempFile();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "EU123456",
            "EU",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "initial", "Initial sequence", DateTime.UtcNow)],
            Path.GetTempPath(),
            EctdTemplateRegistry.EuTemplateKey);
        var document = SubmissionDocument.Rehydrate(documentId, "cover.pdf", "application/pdf", 10, "sha-cover", "md5-cover", filePath, DateTime.UtcNow);
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

        Assert.True(report.IsReady);
        Assert.Equal("Ready", report.Status);
        Assert.Empty(report.Findings);
        Assert.Empty(report.MissingMetadataFields);
        Assert.DoesNotContain(report.Findings, finding => finding.Code.StartsWith("US_REGIONAL", StringComparison.OrdinalIgnoreCase));
    }

    private static PublishReadinessService CreateService(
        SubmissionApplication? application,
        IReadOnlyCollection<DocumentPlacement> placements,
        IReadOnlyCollection<SubmissionDocument> documents,
        IReadOnlyCollection<IEctdValidationRule>? rules = null)
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
        var standardsProfileProvider = new CompositeStandardsProfileProvider(
        [
            new FdaEctd322StandardsProfileProvider(),
            new EuEctd322StandardsProfileProvider()
        ]);
        var packageModelBuilder = new EctdPackageModelBuilder(
            applicationRepository,
            placementRepository,
            documentRepository,
            standardsProfileProvider);

        return new PublishReadinessService(
            validationService,
            packageModelBuilder,
            new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry(
            [
                new UsRegionalBackboneWriter(new UsRegionalXmlWriter()),
                new EuRegionalBackboneWriter(new EuRegionalXmlWriter())
            ]),
            new EctdXmlValidator(),
            standardsProfileProvider,
            new EctdValidationEngine(new RegionalEctdRuleSetProvider(rules ?? [new FileNamingConventionRule()])));
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

    private sealed class FakePdfInspector(PdfInspectionResult result) : IPdfInspector
    {
        public PdfInspectionResult Inspect(Stream pdfStream, string relativeHref)
            => result;
    }
}
