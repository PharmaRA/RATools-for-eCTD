using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Application.Validation.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishJobService(
    IPublishJobRepository repository,
    IBackboneService backboneService,
    ISequenceValidationService validationService,
    IAuditLogService auditLogService) : IPublishJobService
{
    private const string PublishExecutionReportVersion = "1.1";

    public async Task<PublishJobDto> CreateAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteInternalAsync(request, cancellationToken);
        return result.Job.ToDto();
    }

    public async Task<PublishExecutionReportDto> ExecuteAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteInternalAsync(request, cancellationToken);
        stopwatch.Stop();

        var jobDto = result.Job.ToDto();
        var errorCount = result.ValidationReport.Issues.Count(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var warningCount = result.ValidationReport.Issues.Count(x => string.Equals(x.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        var warningSummary = BuildWarningSummary(result.ValidationReport);
        var auditSummary = await BuildAuditSummaryAsync(jobDto, request.SequenceNumber, cancellationToken);

        var report = new PublishExecutionReportDto(
            PublishExecutionReportVersion,
            request.ApplicationId,
            request.SequenceNumber,
            result.ValidationReport.ValidationProfile,
            result.ReportPath,
            result.ValidationReport,
            jobDto,
            stopwatch.ElapsedMilliseconds,
            null,
            auditSummary,
            errorCount,
            warningCount,
            warningSummary,
            result.Job.Status == PublishJobStatus.Completed,
            result.Message);

        report = report with { ArtifactSummary = BuildArtifactSummary(jobDto) };

        if (!string.IsNullOrWhiteSpace(report.ReportPath) && !string.IsNullOrWhiteSpace(jobDto.PackagePath))
        {
            await WriteFinalReportAsync(report, jobDto.PackagePath, cancellationToken);
        }

        return report;
    }

    public async Task<PublishExecutionReportDto?> GetExecutionReportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Status != PublishJobStatus.Completed)
        {
            throw new PublishJobNotReadyException($"Publish job {id} is in status '{job.Status}' and does not have a final report yet.");
        }

        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            throw new PublishJobReportUnavailableException($"Publish job {id} completed without an output path.");
        }

        var outputDirectory = Path.GetDirectoryName(job.OutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new PublishJobReportUnavailableException($"Publish output directory for job {id} no longer exists.");
        }

        var expectedReportPath = Path.Combine(outputDirectory, $"publish-report-{job.SequenceNumber}-{job.Id:N}.json");
        if (!File.Exists(expectedReportPath))
        {
            throw new PublishJobReportUnavailableException($"Publish report for job {id} was not found at '{expectedReportPath}'.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(expectedReportPath, cancellationToken);
            var report = JsonSerializer.Deserialize<PublishExecutionReportDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return report ?? throw new PublishJobReportCorruptedException($"Publish report for job {id} could not be deserialized.");
        }
        catch (JsonException exception)
        {
            throw new PublishJobReportCorruptedException($"Publish report for job {id} is corrupted: {exception.Message}");
        }
    }

    public async Task<PublishArtifactsDto?> GetArtifactsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var outputPath = job.OutputPath;
        var reportPath = outputPath is null
            ? null
            : Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty, $"publish-report-{job.SequenceNumber}-{job.Id:N}.json");

        var artifacts = new List<PublishArtifactDto>
        {
            BuildArtifact("BackboneXml", "file", outputPath),
            BuildArtifact("PublishReport", "file", reportPath),
            BuildArtifact("PackageZip", "file", job.PackagePath)
        };

        return new PublishArtifactsDto(job.Id, job.ApplicationId, job.SequenceNumber, artifacts);
    }

    public async Task<PublishArtifactDownloadDto?> GetArtifactDownloadAsync(Guid id, string artifactName, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Status != PublishJobStatus.Completed)
        {
            throw new PublishJobNotReadyException($"Publish job {id} is in status '{job.Status}' and artifacts are not available yet.");
        }

        var artifact = ResolveArtifact(job, artifactName);
        if (artifact is null)
        {
            throw new PublishArtifactNotSupportedException($"Artifact '{artifactName}' is not supported.");
        }

        if (!artifact.Exists)
        {
            throw new PublishJobReportUnavailableException($"Artifact '{artifactName}' for job {id} was not found.");
        }

        await TryWriteAuditAsync(
            entityType: "PublishJobArtifact",
            entityId: $"{id}:{artifact.Name}",
            action: "Downloaded",
            details: $"Path={artifact.Path}; ContentType={artifact.ContentType}",
            cancellationToken);

        return new PublishArtifactDownloadDto(
            artifact.Name,
            Path.GetFileName(artifact.Path!),
            artifact.Path!,
            artifact.ContentType);
    }

    private static PublishArtifactDto BuildArtifact(string name, string type, string? path)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var sizeBytes = exists ? new FileInfo(path!).Length : 0;
        return new PublishArtifactDto(name, type, path, exists, sizeBytes, GetContentType(name, path));
    }

    private static PublishArtifactDto? ResolveArtifact(PublishJob job, string artifactName)
    {
        var outputPath = job.OutputPath;
        var reportPath = outputPath is null
            ? null
            : Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty, $"publish-report-{job.SequenceNumber}-{job.Id:N}.json");

        if (string.Equals(artifactName, "BackboneXml", StringComparison.OrdinalIgnoreCase))
        {
            return BuildArtifact("BackboneXml", "file", outputPath);
        }

        if (string.Equals(artifactName, "PublishReport", StringComparison.OrdinalIgnoreCase))
        {
            return BuildArtifact("PublishReport", "file", reportPath);
        }

        if (string.Equals(artifactName, "PackageZip", StringComparison.OrdinalIgnoreCase))
        {
            return BuildArtifact("PackageZip", "file", job.PackagePath);
        }

        return null;
    }

    private static string GetContentType(string artifactName, string? path)
    {
        if (string.Equals(artifactName, "BackboneXml", StringComparison.OrdinalIgnoreCase))
        {
            return "application/xml";
        }

        if (string.Equals(artifactName, "PublishReport", StringComparison.OrdinalIgnoreCase))
        {
            return "application/json";
        }

        if (string.Equals(artifactName, "PackageZip", StringComparison.OrdinalIgnoreCase))
        {
            return "application/zip";
        }

        return "application/octet-stream";
    }

    private static string? BuildWarningSummary(ValidationReportDto validationReport)
    {
        var warningMessages = validationReport.Issues
            .Where(x => string.Equals(x.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Code}: {x.Message}")
            .ToArray();

        if (warningMessages.Length == 0)
        {
            return null;
        }

        var shown = warningMessages.Take(3).ToArray();
        var summary = string.Join(" | ", shown);

        var remaining = warningMessages.Length - shown.Length;
        if (remaining > 0)
        {
            summary = $"{summary} | +{remaining} more warning(s)";
        }

        return summary;
    }

    private static PublishArtifactSummaryDto? BuildArtifactSummary(PublishJobDto publishJob)
    {
        if (string.IsNullOrWhiteSpace(publishJob.OutputPath) || !File.Exists(publishJob.OutputPath))
        {
            return null;
        }

        var outputFile = new FileInfo(publishJob.OutputPath);
        var outputDir = outputFile.Directory;
        if (outputDir is null || !outputDir.Exists)
        {
            return null;
        }

        var allFiles = outputDir.GetFiles("*", SearchOption.AllDirectories);
        var totalSize = allFiles.Sum(x => x.Length);

        long packageSize = 0;
        if (!string.IsNullOrWhiteSpace(publishJob.PackagePath) && File.Exists(publishJob.PackagePath))
        {
            packageSize = new FileInfo(publishJob.PackagePath).Length;
        }

        return new PublishArtifactSummaryDto(allFiles.Length, totalSize, packageSize);
    }

    private async Task<PublishAuditSummaryDto?> BuildAuditSummaryAsync(
        PublishJobDto publishJob,
        string sequenceNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var allAuditLogs = await auditLogService.ListAsync(cancellationToken);

            var publishJobEvents = allAuditLogs
                .Where(x => x.EntityType == "PublishJob" && x.EntityId == publishJob.Id.ToString())
                .OrderByDescending(x => x.CreatedUtc)
                .ToArray();

            var validationEvents = allAuditLogs
                .Where(x => x.EntityType == "SequenceValidation" && x.EntityId == $"{publishJob.ApplicationId}:{sequenceNumber}")
                .ToArray();

            return new PublishAuditSummaryDto(
                publishJobEvents.Length,
                validationEvents.Length,
                publishJobEvents.FirstOrDefault()?.Action,
                publishJobEvents.FirstOrDefault()?.CreatedUtc);
        }
        catch
        {
            return null;
        }
    }

    private async Task<(PublishJob Job, ValidationReportDto ValidationReport, string? Message, string? ReportPath)> ExecuteInternalAsync(
        CreatePublishJobRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureNoActivePublishAsync(request, cancellationToken);

        ValidationReportDto? validationReport = null;
        string? reportPath = null;
        var job = new PublishJob(request.ApplicationId, request.SequenceNumber);
        await repository.AddAsync(job, cancellationToken);
        await TryWriteAuditAsync(
            entityType: "PublishJob",
            entityId: job.Id.ToString(),
            action: "Created",
            details: $"Publish job created for application {request.ApplicationId}, sequence {request.SequenceNumber}.",
            cancellationToken);

        try
        {
            validationReport = await validationService.ValidateAsync(
                new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            if (!validationReport.IsValid)
            {
                var failureMessage = string.Join(" | ", validationReport.Issues.Select(x => $"{x.Code}: {x.Message}"));
                job.MarkFailed(failureMessage);
                await repository.UpdateAsync(job, cancellationToken);
                await TryWriteAuditAsync(
                    entityType: "PublishJob",
                    entityId: job.Id.ToString(),
                    action: "ValidationFailed",
                    details: $"Profile={validationReport.ValidationProfile}; {failureMessage}",
                    cancellationToken);
                return (job, validationReport, "Publish stopped because validation failed.", reportPath);
            }

            job.MarkRunning();
            await repository.UpdateAsync(job, cancellationToken);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Started",
                details: $"Publish execution started. Profile={validationReport.ValidationProfile}",
                cancellationToken);

            var generated = await backboneService.GenerateAsync(
                new GenerateBackboneRequest(
                    request.ApplicationId,
                    request.SequenceNumber,
                    $"publish-report-{request.SequenceNumber}-{job.Id:N}.json",
                    $"{request.SequenceNumber}-{job.Id:N}.zip"),
                cancellationToken);
            reportPath = generated.ReportPath;

            job.MarkCompleted(generated.FilePath, generated.PackagePath);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Completed",
                details: $"Profile={validationReport.ValidationProfile}; Output: {generated.FilePath}; Package: {generated.PackagePath}",
                cancellationToken);

            await repository.UpdateAsync(job, cancellationToken);
            return (job, validationReport, "Publish completed successfully.", reportPath);
        }
        catch (Exception exception)
        {
            job.MarkFailed(exception.Message);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Failed",
                details: validationReport is null
                    ? exception.Message
                    : $"Profile={validationReport.ValidationProfile}; {exception.Message}",
                cancellationToken);

            await repository.UpdateAsync(job, cancellationToken);
            validationReport ??= await validationService.ValidateAsync(
                new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            return (job, validationReport, "Publish failed during execution.", reportPath);
        }
    }

    private async Task EnsureNoActivePublishAsync(CreatePublishJobRequest request, CancellationToken cancellationToken)
    {
        var pending = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(request.ApplicationId, request.SequenceNumber, PublishJobStatus.Pending.ToString(), null, null, 1, 1),
            cancellationToken);

        if (pending.TotalCount > 0)
        {
            throw new PublishJobAlreadyInProgressException(
                $"A publish job is already pending for application {request.ApplicationId}, sequence {request.SequenceNumber}.");
        }

        var running = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(request.ApplicationId, request.SequenceNumber, PublishJobStatus.Running.ToString(), null, null, 1, 1),
            cancellationToken);

        if (running.TotalCount > 0)
        {
            throw new PublishJobAlreadyInProgressException(
                $"A publish job is already running for application {request.ApplicationId}, sequence {request.SequenceNumber}.");
        }
    }

    private static async Task WriteFinalReportAsync(
        PublishExecutionReportDto report,
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(report.ReportPath))
        {
            return;
        }

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(report.ReportPath, json, cancellationToken);

        var outputDirectory = Path.GetDirectoryName(report.ReportPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return;
        }

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(outputDirectory, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private async Task TryWriteAuditAsync(
        string entityType,
        string entityId,
        string action,
        string? details,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditLogService.CreateAsync(
                new CreateAuditLogRequest(entityType, entityId, action, "system", details),
                cancellationToken);
        }
        catch
        {
            // Audit logging must not block publish execution.
        }
    }

    public async Task<PublishJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        return job?.ToDto();
    }

    public async Task<IReadOnlyCollection<PublishJobDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await repository.ListAsync(cancellationToken);
        return jobs.Select(x => x.ToDto()).ToArray();
    }
}

internal static class PublishJobMapping
{
    public static PublishJobDto ToDto(this PublishJob job)
    {
        return new PublishJobDto(
            job.Id,
            job.ApplicationId,
            job.SequenceNumber,
            job.Status.ToString(),
            job.OutputPath,
            job.PackagePath,
            job.CreatedUtc,
            job.CompletedUtc,
            job.FailureReason);
    }
}
