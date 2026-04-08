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
    IAuditLogService auditLogService,
    IValidationProfileProvider validationProfileProvider) : ISequenceValidationService
{
    public async Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssueDto>();
        var profileName = validationProfileProvider.ProfileName;
        var validationMode = validationProfileProvider.Mode;

        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            issues.Add(new ValidationIssueDto("Error", "APP_NOT_FOUND", $"Application {request.ApplicationId} was not found."));
            return new ValidationReportDto(request.ApplicationId, request.SequenceNumber, profileName, false, issues);
        }

        var sequence = application.Sequences.SingleOrDefault(x => x.SequenceNumber == request.SequenceNumber);
        if (sequence is null)
        {
            issues.Add(new ValidationIssueDto("Error", "SEQ_NOT_FOUND", $"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}."));
            return new ValidationReportDto(request.ApplicationId, request.SequenceNumber, profileName, false, issues);
        }

        var latestSequenceNumber = application.Sequences
            .Select(x => x.SequenceNumber)
            .OrderBy(x => x, StringComparer.Ordinal)
            .LastOrDefault();

        if (validationMode == ValidationMode.Strict && !string.IsNullOrWhiteSpace(latestSequenceNumber) && latestSequenceNumber != request.SequenceNumber)
        {
            issues.Add(new ValidationIssueDto(
                "Warning",
                "SEQUENCE_NOT_LATEST",
                $"Sequence {request.SequenceNumber} is not the latest sequence ({latestSequenceNumber}) for this application."));
        }

        var placements = await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
        if (placements.Count == 0)
        {
            issues.Add(new ValidationIssueDto("Error", "NO_PLACEMENTS", "The sequence has no document placements."));
        }

        if (validationMode == ValidationMode.Strict)
        {
            var duplicatePlacements = placements
                .GroupBy(x => new { x.DocumentId, Section = x.CtdSection.ToLowerInvariant() })
                .Where(x => x.Count() > 1)
                .ToArray();

            foreach (var duplicate in duplicatePlacements)
            {
                issues.Add(new ValidationIssueDto(
                    "Warning",
                    "DUPLICATE_PLACEMENT",
                    $"Document {duplicate.Key.DocumentId} appears multiple times in section {duplicate.Key.Section}."));
            }
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
            else if (IsInvalidSectionPath(placement.CtdSection))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "INVALID_SECTION_PATH",
                    $"Section '{placement.CtdSection}' is not a valid CTD section path."));
            }
            else
            {
                var firstSegment = placement.CtdSection
                    .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();

                if (validationMode == ValidationMode.Strict &&
                    !string.IsNullOrWhiteSpace(firstSegment) &&
                    !IsValidModulePrefix(firstSegment))
                {
                    issues.Add(new ValidationIssueDto(
                        "Warning",
                        "SECTION_MODULE",
                        $"Section '{placement.CtdSection}' does not use a supported CTD module prefix (m1-m5)."));
                }

                if (validationMode == ValidationMode.Strict &&
                    !placement.CtdSection.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssueDto(
                        "Warning",
                        "SECTION_FORMAT",
                        $"Section '{placement.CtdSection}' does not start with a module prefix (e.g., m1, m5)."));
                }

                var sectionDepth = placement.CtdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                if (validationMode == ValidationMode.Strict && sectionDepth < 2)
                {
                    issues.Add(new ValidationIssueDto(
                        "Warning",
                        "SECTION_GRANULARITY",
                        $"Section '{placement.CtdSection}' may be too coarse; consider a deeper CTD node."));
                }
            }

            if (validationMode == ValidationMode.Strict && string.IsNullOrWhiteSpace(placement.Title))
            {
                issues.Add(new ValidationIssueDto(
                    "Warning",
                    "TITLE_FALLBACK_USED",
                    $"Placement for document {document.FileName} has no explicit title, so the file name will be used in the backbone."));
            }

            var expectedMediaType = GuessMediaTypeByFileName(document.FileName);
            if (validationMode == ValidationMode.Strict &&
                !string.IsNullOrWhiteSpace(expectedMediaType) &&
                !string.Equals(expectedMediaType, document.MediaType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssueDto(
                    "Warning",
                    "MEDIA_TYPE_MISMATCH",
                    $"Document {document.FileName} media type '{document.MediaType}' does not match expected '{expectedMediaType}'."));
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
        var report = new ValidationReportDto(request.ApplicationId, request.SequenceNumber, profileName, isValid, issues);

        await TryWriteAuditAsync(report, cancellationToken);
        return report;
    }

    private async Task TryWriteAuditAsync(ValidationReportDto report, CancellationToken cancellationToken)
    {
        try
        {
            var action = report.IsValid ? "ValidationPassed" : "ValidationFailed";
            var details = $"Profile={report.ValidationProfile}; Issue count: {report.Issues.Count}";

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

    private static bool IsValidModulePrefix(string sectionSegment)
    {
        return sectionSegment.Equals("m1", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m2", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m3", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m4", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidSectionPath(string ctdSection)
    {
        if (ctdSection.Contains("..", StringComparison.Ordinal) ||
            ctdSection.StartsWith(".", StringComparison.Ordinal) ||
            ctdSection.EndsWith(".", StringComparison.Ordinal))
        {
            return true;
        }

        var parts = ctdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        if (!IsValidModulePrefix(parts[0]))
        {
            return true;
        }

        return parts.Skip(1).Any(part => !IsValidSectionSegment(part));
    }

    private static bool IsValidSectionSegment(string part)
    {
        if (int.TryParse(part, out _))
        {
            return true;
        }

        return part.Equals("p", StringComparison.OrdinalIgnoreCase)
               || part.Equals("s", StringComparison.OrdinalIgnoreCase)
               || part.Equals("r", StringComparison.OrdinalIgnoreCase)
               || part.Equals("a", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GuessMediaTypeByFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            _ => null
        };
    }
}
