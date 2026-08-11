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
/// EU 端到端发布回归：readiness（传 profile）与 publish（曾不传 profile）此前使用
/// 不同的 DTD 白名单，EU 发布在 BackboneService 处必败而无任何测试覆盖。
/// 本测试钉死"EU 应用从上传的 m1 文档到发布成功产出交付包"的完整链路。
/// </summary>
public sealed class EuPublishEndToEndTests
{
    [Fact]
    public async Task ExecuteAsync_PublishesEuSequenceEndToEnd()
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

            Assert.True(report.Succeeded, $"EU publish failed: {report.Message}; FailureReason={report.PublishJob.FailureReason}");
            Assert.Equal(PublishJobStatus.Completed.ToString(), report.PublishJob.Status);

            var outputDirectory = Path.GetDirectoryName(report.PublishJob.OutputPath);
            Assert.NotNull(outputDirectory);
            // 交付包必须同时含 ICH 主干、EU 区域 backbone 与按规范文件夹放置的文档。
            Assert.True(File.Exists(Path.Combine(outputDirectory!, "index.xml")));
            Assert.True(File.Exists(Path.Combine(outputDirectory!, "m1", "eu", "eu-regional.xml")));
            Assert.True(File.Exists(Path.Combine(outputDirectory!, "util", "dtd", "eu-regional.dtd")));
            Assert.True(File.Exists(report.PublishJob.PackagePath));

            var euRegionalContent = await File.ReadAllTextAsync(Path.Combine(outputDirectory!, "m1", "eu", "eu-regional.xml"));
            Assert.Contains("m1-eu-regional", euRegionalContent, StringComparison.Ordinal);
            Assert.Contains("cover-letter.pdf", euRegionalContent, StringComparison.Ordinal);
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
        var packageModelBuilder = new EctdPackageModelBuilder(
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
