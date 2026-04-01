using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishJobService(
    IPublishJobRepository repository,
    IBackboneService backboneService,
    ISequenceValidationService validationService,
    IAuditLogService auditLogService) : IPublishJobService
{
    public async Task<PublishJobDto> CreateAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
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
            var validationReport = await validationService.ValidateAsync(
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
                    details: failureMessage,
                    cancellationToken);
                return job.ToDto();
            }

            job.MarkRunning();
            await repository.UpdateAsync(job, cancellationToken);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Started",
                details: "Publish execution started.",
                cancellationToken);

            var generated = await backboneService.GenerateAsync(
                new GenerateBackboneRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            job.MarkCompleted(generated.FilePath, generated.PackagePath);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Completed",
                details: $"Output: {generated.FilePath}; Package: {generated.PackagePath}",
                cancellationToken);
        }
        catch (Exception exception)
        {
            job.MarkFailed(exception.Message);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Failed",
                details: exception.Message,
                cancellationToken);
        }

        await repository.UpdateAsync(job, cancellationToken);
        return job.ToDto();
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
