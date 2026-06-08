using System.IO.Compression;
using RATools.Application.Abstractions.Persistence;
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
            var service = new PublishJobService(repository, backbone, validation, new NoopAuditLogService(), new PublishOutputVerifier());

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
            var xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink" />
                """;

            await File.WriteAllTextAsync(backbonePath, xml, cancellationToken);
            await File.WriteAllTextAsync(reportPath, "{}", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "leaf.txt"), "leaf", cancellationToken);
            System.IO.Compression.ZipFile.CreateFromDirectory(outputDir, packagePath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            return new GeneratedBackboneDto(request.ApplicationId, request.SequenceNumber, "index.xml", backbonePath, reportPath, packagePath, xml);
        }
    }

    private sealed class NoopAuditLogService : IAuditLogService
    {
        public Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, request.Actor, request.Details, DateTime.UtcNow));

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>(Array.Empty<AuditLogDto>());
    }
}
