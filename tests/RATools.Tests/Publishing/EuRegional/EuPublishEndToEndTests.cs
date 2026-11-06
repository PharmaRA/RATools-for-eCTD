using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Publishing;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Rules;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;

using RATools.Tests.TestDoubles;

namespace RATools.Tests.Publishing.EuRegional;

/// <summary>
/// 共享字段不能推导 EU envelope 和文档 country；缺少显式 regional head 时，
/// 发布应在 readiness 阶段拒绝，保留原文件且不产生 backbone 或交付包。
/// </summary>
public sealed class EuPublishEndToEndTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsSharedOnlyEuSequenceWithoutRegionalHead()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-eu-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var applicationId = Guid.Parse("40000000-0000-0000-0000-000000000001");
            const string sequenceNumber = "0000";
            const string applicationNumber = "EU123456";

            // 用与生产一致的 resolver 计算 EU 规范文件夹（m1.0 → m1/eu/10-cover），
            // 文档物理路径与 backbone href 由同一映射推导。
            var resolver = new EctdWorkspacePathResolver();
            var resolution = resolver.Resolve(EctdTemplateRegistry.EuTemplateKey, "m1.0");
            var sequenceRoot = Path.Combine(root, "workspace", applicationNumber, sequenceNumber);
            var documentDirectory = Path.Combine(sequenceRoot, resolution.RelativeFolderPath);
            Directory.CreateDirectory(documentDirectory);
            var documentPath = Path.Combine(documentDirectory, "cover-letter.pdf");
            await File.WriteAllTextAsync(documentPath, "eu-cover-letter-payload");

            var applicationRepository = new InMemoryApplicationRepository();
            var documentRepository = new InMemoryDocumentRepository();
            var placementRepository = new InMemoryDocumentPlacementRepository();
            var publishJobRepository = new InMemoryPublishJobRepository();

            var application = SubmissionApplication.Rehydrate(
                applicationId,
                applicationNumber,
                "EU",
                "Acme Pharma",
                DateTime.UtcNow,
                [SubmissionSequence.Rehydrate(sequenceNumber, "initial", "Initial EU sequence", DateTime.UtcNow, null)],
                Path.Combine(root, "workspace", applicationNumber),
                EctdTemplateRegistry.EuTemplateKey);
            await applicationRepository.AddAsync(application);

            var documentId = Guid.Parse("50000000-0000-0000-0000-000000000001");
            var document = SubmissionDocument.Rehydrate(
                documentId,
                "cover-letter.pdf",
                "application/pdf",
                new FileInfo(documentPath).Length,
                "sha-eu-cover",
                string.Empty,
                documentPath,
                DateTime.UtcNow);
            await documentRepository.AddAsync(document);

            var placement = DocumentPlacement.Rehydrate(
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                documentId,
                applicationId,
                sequenceNumber,
                "m1.0",
                DocumentPlacementOperation.New,
                "EU Cover Letter",
                null,
                DateTime.UtcNow);
            await placementRepository.AddAsync(placement);

            var service = CreateEuPublishJobService(
                applicationRepository,
                documentRepository,
                placementRepository,
                publishJobRepository,
                root);

            var report = await service.ExecuteAsync(new CreatePublishJobRequest(applicationId, sequenceNumber));

            Assert.False(report.Succeeded);
            Assert.Equal(PublishJobStatus.Failed.ToString(), report.PublishJob.Status);
            Assert.Contains("regionalMetadata.eu", report.Message + report.PublishJob.FailureReason, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(report.PublishJob.OutputPath));
            Assert.True(string.IsNullOrEmpty(report.PublishJob.PackagePath));
            Assert.Empty(Directory.EnumerateFiles(root, "eu-regional.xml", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories));
            Assert.Equal("eu-cover-letter-payload", await File.ReadAllTextAsync(documentPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static PublishJobService CreateEuPublishJobService(
        InMemoryApplicationRepository applicationRepository,
        InMemoryDocumentRepository documentRepository,
        InMemoryDocumentPlacementRepository placementRepository,
        InMemoryPublishJobRepository publishJobRepository,
        string root)
    {
        // 与生产 DI 相同的组合根形态：Composite provider + 双区域 writer 注册表 + 共享规则集。
        var standardsProfileProvider = new CompositeStandardsProfileProvider(
        [
            new FdaEctd322StandardsProfileProvider(),
            new EuEctd322StandardsProfileProvider(),
        ]);
        var packageModelBuilder = PackageTestServices.Create(
            applicationRepository,
            placementRepository,
            documentRepository,
            standardsProfileProvider,
            PermissiveDocumentStorageBoundary.Instance);
        var regionalWriterRegistry = new RegionalBackboneWriterRegistry(
        [
            new UsRegionalBackboneWriter(new UsRegionalXmlWriter()),
            new EuRegionalBackboneWriter(new EuRegionalXmlWriter()),
        ]);
        var backboneService = new BackboneService(
            packageModelBuilder,
            new IchIndexXmlWriter(),
            regionalWriterRegistry,
            new EctdXmlValidator(),
            standardsProfileProvider,
            new LocalBackboneFileWriter(
                Options.Create(new BackboneOutputOptions { RootPath = root }),
                NullLogger<LocalBackboneFileWriter>.Instance));
        var auditLogService = new NoopAuditLogService();
        var validationService = new SequenceValidationService(
            applicationRepository,
            placementRepository,
            documentRepository,
            auditLogService,
            new RelaxedValidationProfileProvider(),
            NullLogger<SequenceValidationService>.Instance,
            PermissiveDocumentStorageBoundary.Instance);
        var publishReadinessService = new PublishReadinessService(
            validationService,
            packageModelBuilder,
            new IchIndexXmlWriter(),
            regionalWriterRegistry,
            new EctdXmlValidator(),
            standardsProfileProvider,
            new EctdValidationEngine(new RegionalEctdRuleSetProvider([new FileNamingConventionRule()])));
        var artifactStore = new LocalPublishArtifactStore(new AllowAllWorkspacePathPolicy());

        return new PublishJobService(
            publishJobRepository,
            backboneService,
            validationService,
            publishReadinessService,
            auditLogService,
            new PublishArtifactResolver(artifactStore),
            new PublishReportStore(artifactStore),
            new PublishOutputVerifier(),
            new FakePublishJobQueue(),
            NullLogger<PublishJobService>.Instance);
    }

    private sealed class NoopAuditLogService : IAuditLogService
    {
        public Task<AuditLogDto> WriteSystemEventAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, "system", request.Details, DateTime.UtcNow));

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);

        public Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
            IReadOnlyCollection<(string EntityType, string EntityId)> entities,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);

        public Task<AuditLogPageDto> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogPageDto(query.Page, query.PageSize, 0, []));
    }

    private sealed class RelaxedValidationProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;

        public ValidationMode Mode => ValidationMode.Relaxed;
    }

    private sealed class AllowAllWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path) => Path.GetFullPath(path);
    }
}
