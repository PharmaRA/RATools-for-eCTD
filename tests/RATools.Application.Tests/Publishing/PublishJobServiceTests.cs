using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
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
using Xunit;

namespace RATools.Application.Tests.Publishing;

public sealed class PublishJobServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UsesJobSpecificReportFileName()
    {
        var repository = new StubPublishJobRepository();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ratools-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var outputDirectory = Path.Combine(tempRoot, "0000");
            Directory.CreateDirectory(outputDirectory);

            var outputPath = Path.Combine(outputDirectory, "index.xml");
            var reportPath = Path.Combine(outputDirectory, "publish-report.json");
            var packagePath = Path.Combine(tempRoot, "0000.zip");
            await File.WriteAllTextAsync(outputPath, "<ectd />");
            await File.WriteAllTextAsync(reportPath, "{}");
            await File.WriteAllTextAsync(packagePath, "zip");

            var service = new PublishJobService(
                repository,
                new StubBackboneService(outputPath, reportPath, packagePath),
                new StubValidationService(),
                new StubAuditLogService());

            var result = await service.ExecuteAsync(new CreatePublishJobRequest(Guid.NewGuid(), "0000"));

            Assert.NotNull(result.ReportPath);
            Assert.DoesNotContain("publish-report.json", result.ReportPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.PublishJob.Id.ToString("N"), result.ReportPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetExecutionReportAsync_ReadsPersistedReportJson()
    {
        var repository = new StubPublishJobRepository();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ratools-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var reportPath = Path.Combine(tempRoot, "publish-report-0000-00000000000000000000000000000002.json");
            await File.WriteAllTextAsync(reportPath, "{\"reportVersion\":\"1.1\",\"applicationId\":\"00000000-0000-0000-0000-000000000001\",\"sequenceNumber\":\"0000\",\"validationProfile\":\"default-v1\",\"reportPath\":\"x\",\"validationReport\":{\"applicationId\":\"00000000-0000-0000-0000-000000000001\",\"sequenceNumber\":\"0000\",\"validationProfile\":\"default-v1\",\"isValid\":true,\"issues\":[]},\"publishJob\":{\"id\":\"00000000-0000-0000-0000-000000000002\",\"applicationId\":\"00000000-0000-0000-0000-000000000001\",\"sequenceNumber\":\"0000\",\"status\":\"Completed\",\"outputPath\":null,\"packagePath\":null,\"createdUtc\":\"2026-01-01T00:00:00Z\",\"completedUtc\":null,\"failureReason\":null},\"durationMs\":1,\"artifactSummary\":null,\"auditSummary\":null,\"errorCount\":0,\"warningCount\":0,\"warningSummary\":null,\"succeeded\":true,\"message\":\"ok\"}");

            var job = PublishJob.Rehydrate(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "0000",
                PublishJobStatus.Completed,
                Path.Combine(tempRoot, "index.xml"),
                Path.Combine(tempRoot, "0000.zip"),
                DateTime.UtcNow,
                DateTime.UtcNow,
                null);

            await repository.AddAsync(job);

            var service = new PublishJobService(
                repository,
                new StubBackboneService(Path.Combine(tempRoot, "index.xml"), reportPath, Path.Combine(tempRoot, "0000.zip")),
                new StubValidationService(),
                new StubAuditLogService());

            var result = await service.GetExecutionReportAsync(job.Id);

            Assert.NotNull(result);
            Assert.Equal("1.1", result!.ReportVersion);
            Assert.Equal("0000", result.SequenceNumber);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetExecutionReportAsync_ThrowsWhenJobIsNotCompleted()
    {
        var repository = new StubPublishJobRepository();
        var job = PublishJob.Rehydrate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0000",
            PublishJobStatus.Running,
            Path.Combine(Path.GetTempPath(), "index.xml"),
            Path.Combine(Path.GetTempPath(), "0000.zip"),
            DateTime.UtcNow,
            null,
            null);

        await repository.AddAsync(job);

        var service = new PublishJobService(
            repository,
            new StubBackboneService(Path.Combine(Path.GetTempPath(), "index.xml"), Path.Combine(Path.GetTempPath(), "publish-report.json"), Path.Combine(Path.GetTempPath(), "0000.zip")),
            new StubValidationService(),
            new StubAuditLogService());

        await Assert.ThrowsAsync<PublishJobNotReadyException>(() => service.GetExecutionReportAsync(job.Id));
    }

    [Fact]
    public async Task GetExecutionReportAsync_ThrowsWhenReportFileIsMissing()
    {
        var repository = new StubPublishJobRepository();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ratools-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var job = PublishJob.Rehydrate(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Guid.NewGuid(),
                "0000",
                PublishJobStatus.Completed,
                Path.Combine(tempRoot, "index.xml"),
                Path.Combine(tempRoot, "0000.zip"),
                DateTime.UtcNow,
                DateTime.UtcNow,
                null);

            await repository.AddAsync(job);

            var service = new PublishJobService(
                repository,
                new StubBackboneService(Path.Combine(tempRoot, "index.xml"), Path.Combine(tempRoot, "publish-report.json"), Path.Combine(tempRoot, "0000.zip")),
                new StubValidationService(),
                new StubAuditLogService());

            await Assert.ThrowsAsync<PublishJobReportUnavailableException>(() => service.GetExecutionReportAsync(job.Id));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetArtifactsAsync_ReturnsKnownArtifactPathsAndExistence()
    {
        var repository = new StubPublishJobRepository();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ratools-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var outputPath = Path.Combine(tempRoot, "index.xml");
            var packagePath = Path.Combine(tempRoot, "0000.zip");
            var reportPath = Path.Combine(tempRoot, "publish-report-0000-00000000000000000000000000000002.json");
            await File.WriteAllTextAsync(outputPath, "<ectd />");
            await File.WriteAllTextAsync(packagePath, "zip");
            await File.WriteAllTextAsync(reportPath, "{}");

            var job = PublishJob.Rehydrate(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Guid.NewGuid(),
                "0000",
                PublishJobStatus.Completed,
                outputPath,
                packagePath,
                DateTime.UtcNow,
                DateTime.UtcNow,
                null);

            await repository.AddAsync(job);

            var service = new PublishJobService(
                repository,
                new StubBackboneService(outputPath, reportPath, packagePath),
                new StubValidationService(),
                new StubAuditLogService());

            var result = await service.GetArtifactsAsync(job.Id);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Artifacts.Count);
            Assert.All(result.Artifacts, x => Assert.True(x.Exists));
            Assert.All(result.Artifacts, x => Assert.True(x.SizeBytes > 0));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

file sealed class StubPublishJobRepository : IPublishJobRepository
{
    private readonly Dictionary<Guid, PublishJob> _jobs = new();

    public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(id, out var job);
        return Task.FromResult(job);
    }

    public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IReadOnlyCollection<PublishJob>)_jobs.Values.ToArray());
    }
}

file sealed class StubBackboneService(string outputPath, string reportPath, string packagePath) : IBackboneService
{
    public Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
    {
        var finalReportPath = Path.Combine(Path.GetDirectoryName(reportPath)!, request.ReportFileName);
        File.Copy(reportPath, finalReportPath, overwrite: true);
        return Task.FromResult(new GeneratedBackboneDto(request.ApplicationId, request.SequenceNumber, "index.xml", outputPath, finalReportPath, packagePath, "<ectd />"));
    }
}

file sealed class StubValidationService : ISequenceValidationService
{
    public Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ValidationReportDto(request.ApplicationId, request.SequenceNumber, "default-v1", true, Array.Empty<ValidationIssueDto>()));
    }
}

file sealed class StubAuditLogService : IAuditLogService
{
    private readonly List<AuditLogDto> _entries = [];

    public Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
    {
        var dto = new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, request.Actor, request.Details, DateTime.UtcNow);
        _entries.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IReadOnlyCollection<AuditLogDto>)_entries.ToArray());
    }
}
