using System.IO.Compression;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;
using Microsoft.Extensions.Logging.Abstractions;

namespace RATools.Tests.Publishing;

public sealed class PublishJobServiceEvidenceTests
{
    [Fact]
    public async Task ExecuteAsync_StoresIntegrityEvidenceInExecutionReport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-service-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var repository = new InMemoryPublishJobRepository();
            var validation = new PassingValidationService();
            var backbone = new EvidenceBackboneService(root);
            var readiness = new PassingPublishReadinessService();
            var artifactStore = new LocalPublishArtifactStore(new AllowAllWorkspacePathPolicy());
            var service = new PublishJobService(
                repository,
                backbone,
                validation,
                readiness,
                new NoopAuditLogService(),
                new PublishArtifactResolver(artifactStore),
                new PublishReportStore(artifactStore),
                new PublishOutputVerifier(),
                new FakePublishJobQueue(),
                NullLogger<PublishJobService>.Instance);

            var report = await service.ExecuteAsync(new CreatePublishJobRequest(Guid.NewGuid(), "0001", root));

            Assert.NotNull(report.IntegrityEvidence);
            Assert.Contains(report.IntegrityEvidence.Artifacts, x => x.Role == "BackboneXml" && x.Exists);
            Assert.Contains(report.IntegrityEvidence.Artifacts, x => x.Role == "PackageZip" && x.Exists);
            Assert.Empty(report.IntegrityEvidence.Findings);

            using var archive = ZipFile.OpenRead(report.PublishJob.PackagePath!);
            var entries = archive.Entries.Select(x => x.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("index.xml", entries);
            Assert.Contains("leaf.txt", entries);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_StopsBeforeBackboneGenerationWhenPublishReadinessBlocks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-service-readiness-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var repository = new InMemoryPublishJobRepository();
            var validation = new PassingValidationService();
            var backbone = new ThrowingBackboneService();
            var readiness = new BlockingPublishReadinessService();
            var service = new PublishJobService(
                repository,
                backbone,
                validation,
                readiness,
                new NoopAuditLogService(),
                new PublishArtifactResolver(new LocalPublishArtifactStore(new AllowAllWorkspacePathPolicy())),
                new PublishReportStore(new LocalPublishArtifactStore(new AllowAllWorkspacePathPolicy())),
                new PublishOutputVerifier(),
                new FakePublishJobQueue(),
                NullLogger<PublishJobService>.Instance);

            var report = await service.ExecuteAsync(new CreatePublishJobRequest(Guid.NewGuid(), "0001", root));

            Assert.False(report.Succeeded);
            Assert.Equal("Failed", report.PublishJob.Status);
            Assert.Contains("publish readiness", report.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Null(report.PublishJob.OutputPath);
            Assert.Null(report.PublishJob.PackagePath);
            Assert.Null(report.ReportPath);
            Assert.NotNull(report.PublishReadiness);
            Assert.False(report.PublishReadiness!.IsReady);
            Assert.Contains(report.PublishReadiness.Findings, x => x.Code == "US_REGIONAL_METADATA_MISSING");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class PassingValidationService : ISequenceValidationService
    {
        public Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ValidationReportDto(
                request.ApplicationId,
                request.SequenceNumber,
                "US FDA eCTD 3.2.2",
                true,
                Array.Empty<ValidationIssueDto>(),
                Array.Empty<ValidationSectionMatchDto>(),
                Array.Empty<ValidationLifecycleMatchDto>()));
        }
    }

    private sealed class BlockingPublishReadinessService : IPublishReadinessService
    {
        public Task<PublishReadinessReportDto> GetAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
        {
            return GetAsync(request, BuildValidationReport(request), cancellationToken);
        }

        public Task<PublishReadinessReportDto> GetAsync(
            ValidateSequenceRequest request,
            ValidationReportDto validationReport,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PublishReadinessReportDto(
                request.ApplicationId,
                request.SequenceNumber,
                false,
                "Blocked",
                1,
                0,
                validationReport,
                ["ApplicantContactName"],
                [
                    new PublishReadinessCategorySummaryDto("RegionalMetadata", 1, 0, 1)
                ],
                [
                    new PublishReadinessFindingDto(
                        "PublishPreflight",
                        "Error",
                        "US_REGIONAL_METADATA_MISSING",
                        "metadata field 'ApplicantContactName' is required.",
                        "RegionalMetadata",
                        "Populate the required US Regional publishing metadata field before publishing.",
                        "ApplicantContactName")
                ]));
        }
    }

    private sealed class PassingPublishReadinessService : IPublishReadinessService
    {
        public Task<PublishReadinessReportDto> GetAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
            => GetAsync(request, BuildValidationReport(request), cancellationToken);

        public Task<PublishReadinessReportDto> GetAsync(
            ValidateSequenceRequest request,
            ValidationReportDto validationReport,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PublishReadinessReportDto(
                request.ApplicationId,
                request.SequenceNumber,
                true,
                "Ready",
                0,
                0,
                validationReport,
                Array.Empty<string>(),
                Array.Empty<PublishReadinessCategorySummaryDto>(),
                Array.Empty<PublishReadinessFindingDto>()));
        }
    }

    private sealed class EvidenceBackboneService(string root) : IBackboneService
    {
        public async Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
        {
            var outputDir = Path.Combine(root, "output");
            var reportDir = Path.Combine(root, "_artifacts", request.SequenceNumber);
            var packageDir = Path.Combine(root, "_packages", request.SequenceNumber);
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(reportDir);
            Directory.CreateDirectory(packageDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(reportDir, request.ReportFileName);
            var packagePath = Path.Combine(packageDir, request.PackageFileName);
            // backbone 必须引用交付的 leaf.txt，否则会被孤儿文件反向扫描（正确地）标记。
            var xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink">
                  <ectd:leaf xlink:href="leaf.txt" />
                </ectd:ectd>
                """;

            await File.WriteAllTextAsync(backbonePath, xml, cancellationToken);
            await File.WriteAllTextAsync(reportPath, "{}", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "leaf.txt"), "leaf", cancellationToken);
            System.IO.Compression.ZipFile.CreateFromDirectory(outputDir, packagePath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            return new GeneratedBackboneDto(request.ApplicationId, request.SequenceNumber, "index.xml", backbonePath, reportPath, packagePath, xml);
        }
    }

    private sealed class ThrowingBackboneService : IBackboneService
    {
        public Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Backbone generation should not run when readiness is blocked.");
    }

    private sealed class NoopAuditLogService : IAuditLogService
    {
        public Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, request.Actor, request.Details, DateTime.UtcNow));

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>(Array.Empty<AuditLogDto>());

        public Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
            IReadOnlyCollection<(string EntityType, string EntityId)> entities,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>(Array.Empty<AuditLogDto>());
    }

    private sealed class AllowAllWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path) => Path.GetFullPath(path);
    }

    private static ValidationReportDto BuildValidationReport(ValidateSequenceRequest request)
    {
        return new ValidationReportDto(
            request.ApplicationId,
            request.SequenceNumber,
            "US FDA eCTD 3.2.2",
            true,
            Array.Empty<ValidationIssueDto>(),
            Array.Empty<ValidationSectionMatchDto>(),
            Array.Empty<ValidationLifecycleMatchDto>());
    }
}
