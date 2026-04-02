using System.Diagnostics;
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
        var artifactSummary = BuildArtifactSummary(jobDto);
        var errorCount = result.ValidationReport.Issues.Count(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var warningCount = result.ValidationReport.Issues.Count(x => string.Equals(x.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        var warningSummary = BuildWarningSummary(result.ValidationReport);
        var auditSummary = await BuildAuditSummaryAsync(jobDto, request.SequenceNumber, cancellationToken);

        return new PublishExecutionReportDto(
            PublishExecutionReportVersion,
            request.ApplicationId,
            request.SequenceNumber,
            result.ValidationReport.ValidationProfile,
            result.ValidationReport,
            jobDto,
            stopwatch.ElapsedMilliseconds,
            artifactSummary,
            auditSummary,
            errorCount,
            warningCount,
            warningSummary,
            result.Job.Status == PublishJobStatus.Completed,
            result.Message);
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

    private async Task<(PublishJob Job, ValidationReportDto ValidationReport, string? Message)> ExecuteInternalAsync(
        CreatePublishJobRequest request,
        CancellationToken cancellationToken)
    {
        ValidationReportDto? validationReport = null;
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
                return (job, validationReport, "Publish stopped because validation failed.");
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
                new GenerateBackboneRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            job.MarkCompleted(generated.FilePath, generated.PackagePath);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Completed",
                details: $"Profile={validationReport.ValidationProfile}; Output: {generated.FilePath}; Package: {generated.PackagePath}",
                cancellationToken);

            await repository.UpdateAsync(job, cancellationToken);
            return (job, validationReport, "Publish completed successfully.");
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

            return (job, validationReport, "Publish failed during execution.");
        }
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
