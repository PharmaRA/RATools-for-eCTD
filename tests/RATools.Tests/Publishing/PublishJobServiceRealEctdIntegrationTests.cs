using System.IO.Compression;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishJobServiceRealEctdIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_PublishesRealEctdDeliveryPackageWithRegionalAndIchArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-real-ectd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var applicationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            const string sequenceNumber = "0001";
            const string applicationNumber = "ANDA123456";
            var publishedDocumentPath = Path.Combine(root, "workspace", applicationNumber, sequenceNumber, "m1", "us", "12-cover-letters", "cover-letter.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(publishedDocumentPath)!);
            await File.WriteAllTextAsync(publishedDocumentPath, "cover-letter-payload");

            var applicationRepository = new InMemoryApplicationRepository();
            var documentRepository = new InMemoryDocumentRepository();
            var placementRepository = new InMemoryDocumentPlacementRepository();
            var publishJobRepository = new InMemoryPublishJobRepository();

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
                applicationNumber,
                "US",
                "Acme Pharma",
                DateTime.UtcNow,
                [SubmissionSequence.Rehydrate(sequenceNumber, "original-application", "Initial sequence", DateTime.UtcNow, metadata)],
                Path.Combine(root, "workspace", applicationNumber),
                EctdTemplateRegistry.DefaultTemplateKey);
            await applicationRepository.AddAsync(application);

            var documentId = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var placementId = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var document = SubmissionDocument.Rehydrate(
                documentId,
                "cover-letter.pdf",
                "application/pdf",
                new FileInfo(publishedDocumentPath).Length,
                "sha-cover-letter",
                publishedDocumentPath,
                DateTime.UtcNow);
            await documentRepository.AddAsync(document);

            var placement = DocumentPlacement.Rehydrate(
                placementId,
                documentId,
                applicationId,
                sequenceNumber,
                "m1.2",
                DocumentPlacementOperation.New,
                "Cover Letter",
                null,
                DateTime.UtcNow);
            await placementRepository.AddAsync(placement);

            var auditLogService = new RecordingAuditLogService();
            var service = CreatePublishJobService(
                applicationRepository,
                documentRepository,
                placementRepository,
                publishJobRepository,
                root,
                auditLogService);

            var report = await service.ExecuteAsync(new CreatePublishJobRequest(applicationId, sequenceNumber, root));

            Assert.True(report.Succeeded);
            Assert.NotNull(report.IntegritySummary);
            Assert.True(report.IntegritySummary!.IsConsistent);
            Assert.NotNull(report.IntegrityEvidence);
            Assert.Empty(report.IntegrityEvidence!.Findings);
            Assert.NotNull(report.ArtifactSummary);
            Assert.True(report.ArtifactSummary!.FileCount >= 5);
            Assert.NotNull(report.AuditSummary);
            Assert.Equal(3, report.AuditSummary!.PublishJobEventCount);
            Assert.Equal(1, report.AuditSummary.ValidationEventCount);
            Assert.Equal("Completed", report.AuditSummary.LatestPublishJobAction);

            Assert.Equal("Completed", report.PublishJob.Status);
            Assert.NotNull(report.PublishJob.OutputPath);
            Assert.NotNull(report.PublishJob.PackagePath);
            Assert.True(File.Exists(report.PublishJob.OutputPath));
            Assert.True(File.Exists(report.PublishJob.PackagePath));
            Assert.True(File.Exists(report.ReportPath));

            var outputDirectory = Path.GetDirectoryName(report.PublishJob.OutputPath!);
            Assert.NotNull(outputDirectory);

            var usRegionalPath = Path.Combine(outputDirectory!, "m1", "us", "us-regional.xml");
            var md5Path = Path.Combine(outputDirectory!, "index-md5.txt");
            var ichDtdPath = Path.Combine(outputDirectory!, "util", "dtd", "ich-ectd-3-2.dtd");
            var usRegionalDtdPath = Path.Combine(outputDirectory!, "util", "dtd", "us-regional-v3-3.dtd");
            var publishedLeafPath = Path.Combine(outputDirectory!, "m1", "us", "12-cover-letters", "cover-letter.pdf");

            Assert.True(File.Exists(usRegionalPath));
            Assert.True(File.Exists(md5Path));
            Assert.True(File.Exists(ichDtdPath));
            Assert.True(File.Exists(usRegionalDtdPath));
            Assert.True(File.Exists(publishedLeafPath));

            var indexXml = await File.ReadAllTextAsync(report.PublishJob.OutputPath!);
            var usRegionalXml = await File.ReadAllTextAsync(usRegionalPath);
            var md5Manifest = await File.ReadAllTextAsync(md5Path);
            var reportJson = await File.ReadAllTextAsync(report.ReportPath!);

            Assert.Contains("util/dtd/ich-ectd-3-2.dtd", indexXml, StringComparison.Ordinal);
            Assert.Contains("../../util/dtd/us-regional-v3-3.dtd", usRegionalXml, StringComparison.Ordinal);
            Assert.Contains("jane.regulatory@example.test", usRegionalXml, StringComparison.Ordinal);
            Assert.Contains("index.xml", md5Manifest, StringComparison.Ordinal);
            Assert.Contains("m1/us/us-regional.xml", md5Manifest, StringComparison.Ordinal);
            Assert.Contains("m1/us/12-cover-letters/cover-letter.pdf", md5Manifest, StringComparison.Ordinal);
            Assert.Contains("\"Succeeded\": true", reportJson, StringComparison.Ordinal);

            using var archive = ZipFile.OpenRead(report.PublishJob.PackagePath!);
            var entries = archive.Entries
                .Select(x => x.FullName.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("index.xml", entries);
            Assert.Contains("m1/us/us-regional.xml", entries);
            Assert.Contains("m1/us/12-cover-letters/cover-letter.pdf", entries);
            Assert.Contains("util/dtd/ich-ectd-3-2.dtd", entries);
            Assert.Contains("util/dtd/us-regional-v3-3.dtd", entries);
            Assert.Contains("index-md5.txt", entries);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static PublishJobService CreatePublishJobService(
        IApplicationRepository applicationRepository,
        IDocumentRepository documentRepository,
        IDocumentPlacementRepository placementRepository,
        IPublishJobRepository publishJobRepository,
        string root,
        IAuditLogService auditLogService)
    {
        var standardsProfileProvider = new FdaEctd322StandardsProfileProvider();
        var packageModelBuilder = new EctdPackageModelBuilder(
            applicationRepository,
            placementRepository,
            documentRepository,
            standardsProfileProvider);
        var backboneService = new BackboneService(
            packageModelBuilder,
            new IchIndexXmlWriter(),
            new UsRegionalXmlWriter(),
            new EctdXmlValidator(),
            new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions { RootPath = root })));
        var validationService = new SequenceValidationService(
            applicationRepository,
            placementRepository,
            documentRepository,
            auditLogService,
            new RelaxedValidationProfileProvider());

        return new PublishJobService(
            publishJobRepository,
            backboneService,
            validationService,
            auditLogService,
            new PublishOutputVerifier());
    }

    private sealed class RecordingAuditLogService : IAuditLogService
    {
        private readonly List<AuditLogDto> _entries = [];

        public Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
        {
            var entry = new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, request.Actor, request.Details, DateTime.UtcNow);
            _entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>(_entries.ToArray());
    }

    private sealed class RelaxedValidationProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;

        public ValidationMode Mode => ValidationMode.Relaxed;
    }
}
