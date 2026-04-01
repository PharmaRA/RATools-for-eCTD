using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;

namespace RATools.Application.Validation;

public sealed class SequenceValidationService(
    IApplicationRepository applicationRepository,
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IAuditLogService auditLogService) : ISequenceValidationService
{
    public async Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssueDto>();

        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            issues.Add(new ValidationIssueDto("Error", "APP_NOT_FOUND", $"Application {request.ApplicationId} was not found."));
            return new ValidationReportDto(request.ApplicationId, request.SequenceNumber, false, issues);
        }

        var sequence = application.Sequences.SingleOrDefault(x => x.SequenceNumber == request.SequenceNumber);
        if (sequence is null)
        {
            issues.Add(new ValidationIssueDto("Error", "SEQ_NOT_FOUND", $"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}."));
            return new ValidationReportDto(request.ApplicationId, request.SequenceNumber, false, issues);
        }

        var placements = await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
        if (placements.Count == 0)
        {
            issues.Add(new ValidationIssueDto("Error", "NO_PLACEMENTS", "The sequence has no document placements."));
        }

        var documents = await documentRepository.ListAsync(cancellationToken);
        var documentById = documents.ToDictionary(x => x.Id, x => x);

        foreach (var placement in placements)
        {
            if (!documentById.TryGetValue(placement.DocumentId, out var document))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "DOCUMENT_NOT_FOUND",
                    $"Referenced document {placement.DocumentId} was not found for section {placement.CtdSection}."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(placement.CtdSection))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "SECTION_MISSING",
                    $"Document {document.FileName} is missing a CTD section."));
            }

            if (!File.Exists(document.StoragePath))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "FILE_MISSING",
                    $"Document file '{document.StoragePath}' does not exist."));
            }
        }

        var isValid = issues.All(x => !string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var report = new ValidationReportDto(request.ApplicationId, request.SequenceNumber, isValid, issues);

        await TryWriteAuditAsync(report, cancellationToken);
        return report;
    }

    private async Task TryWriteAuditAsync(ValidationReportDto report, CancellationToken cancellationToken)
    {
        try
        {
            var action = report.IsValid ? "ValidationPassed" : "ValidationFailed";
            var details = $"Issue count: {report.Issues.Count}";

            await auditLogService.CreateAsync(
                new CreateAuditLogRequest(
                    "SequenceValidation",
                    $"{report.ApplicationId}:{report.SequenceNumber}",
                    action,
                    "system",
                    details),
                cancellationToken);
        }
        catch
        {
            // Audit logging must not block validation.
        }
    }
}
