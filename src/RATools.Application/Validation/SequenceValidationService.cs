using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Application.Documents;
using RATools.Application.Publishing;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Domain.Documents;

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
        var sectionMatches = new List<ValidationSectionMatchDto>();
        var lifecycleMatches = new List<ValidationLifecycleMatchDto>();
        var validationMode = validationProfileProvider.Mode;

        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            issues.Add(new ValidationIssueDto("Error", "APP_NOT_FOUND", $"Application {request.ApplicationId} was not found."));
            return new ValidationReportDto(request.ApplicationId, request.SequenceNumber, SectionDictionaryProfiles.CanonicalUsProfileName, false, issues, sectionMatches, lifecycleMatches);
        }

        var template = EctdTemplateRegistry.Resolve(application.EctdTemplateKey);
        var normalizedProfileName = SectionDictionaryProfiles.NormalizeProfileName(template.ValidationProfileName);
        var resolvedProfile = SectionDictionaryProfiles.ResolveByName(normalizedProfileName);
        var profileName = resolvedProfile.Name;
        var sectionDictionary = new SectionDictionary(resolvedProfile);

        var sequence = application.Sequences.SingleOrDefault(x => x.SequenceNumber == request.SequenceNumber);
        if (sequence is null)
        {
            issues.Add(new ValidationIssueDto("Error", "SEQ_NOT_FOUND", $"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}."));
            return new ValidationReportDto(request.ApplicationId, request.SequenceNumber, profileName, false, issues, sectionMatches, lifecycleMatches);
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
        var applicationPlacements = await placementRepository.ListByApplicationAsync(request.ApplicationId, cancellationToken);
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
        var referencedDocumentIds = placements.Select(x => x.DocumentId).ToHashSet();
        var referencedDocuments = documents.Where(x => referencedDocumentIds.Contains(x.Id)).ToArray();

        var duplicatePublishedPaths = referencedDocuments
            .GroupBy(document => PublishOutputNaming.BuildPublishedDocumentRelativePath(document, request.SequenceNumber), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .ToArray();

        foreach (var duplicatePublishedPath in duplicatePublishedPaths)
        {
            issues.Add(new ValidationIssueDto(
                "Error",
                "DUPLICATE_PUBLISHED_DOCUMENT_PATH",
                $"Multiple documents resolve to the same published path '{duplicatePublishedPath.Key}'."));
        }

        var documentById = documents
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var placement in placements)
        {
            if (!IsSupportedOperation(placement.Operation))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "UNSUPPORTED_OPERATION_VALUE",
                    $"Operation '{placement.Operation}' is not supported for backbone generation."));
                continue;
            }

            if (!documentById.TryGetValue(placement.DocumentId, out var document))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "DOCUMENT_NOT_FOUND",
                    $"Referenced document {placement.DocumentId} was not found for section {placement.CtdSection}."));
                continue;
            }

            if (placement.Operation is DocumentPlacementOperation.Replace or DocumentPlacementOperation.Delete or DocumentPlacementOperation.Append)
            {
                var historicalPlacements = applicationPlacements
                    .Where(x => x.SequenceNumber != request.SequenceNumber)
                    .Where(x => CompareSequenceNumbers(x.SequenceNumber, request.SequenceNumber) < 0)
                    .Where(x => x.CtdSection == placement.CtdSection)
                    .ToArray();

                var resolution = LifecycleTargetResolver.Resolve(
                    placement,
                    document,
                    placements,
                    historicalPlacements,
                    documentById);

                lifecycleMatches.Add(new ValidationLifecycleMatchDto(
                    placement.Operation.ToString(),
                    request.SequenceNumber,
                    placement.CtdSection,
                    placement.DocumentId,
                    resolution.ResultCode,
                    resolution.MatchStrategy,
                    resolution.AttemptedStrategies,
                    resolution.HistoricalMatchCount,
                    resolution.HistoricalSequenceNumbers,
                    resolution.HistoricalPlacementIds,
                    resolution.HistoricalFinalState));

                if (resolution.ResultCode != "MATCHED")
                {
                    issues.Add(new ValidationIssueDto(
                        "Error",
                        resolution.ResultCode,
                        BuildLifecycleErrorMessage(placement, resolution)));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(document.FileName)
                || string.IsNullOrWhiteSpace(document.MediaType)
                || string.IsNullOrWhiteSpace(document.Sha256))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "MISSING_LEAF_CORE_METADATA",
                    $"Document {document.Id} is missing required backbone metadata (file name, media type, or checksum)."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(placement.CtdSection))
            {
                issues.Add(new ValidationIssueDto(
                    "Error",
                    "SECTION_MISSING",
                    $"Document {document.FileName} is missing a CTD section."));
            }
            else
            {
                var sectionMatch = sectionDictionary.Classify(placement.CtdSection);
                if (sectionMatches.All(x => x.SectionPath != placement.CtdSection))
                {
                    sectionMatches.Add(new ValidationSectionMatchDto(
                        placement.CtdSection,
                        sectionMatch.IsValid,
                        sectionMatch.IsStandard,
                        sectionMatch.MatchedPrefix,
                        sectionMatch.Reason));
                }

                if (!sectionMatch.IsValid)
                {
                    issues.Add(new ValidationIssueDto(
                        "Error",
                        "INVALID_SECTION_PATH",
                        $"Section '{placement.CtdSection}' is not a valid CTD section path."));
                }
                else
                {
                    var sectionDepth = placement.CtdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    if (validationMode == ValidationMode.Strict && sectionDepth < 2)
                    {
                        issues.Add(new ValidationIssueDto(
                            "Warning",
                            "SECTION_DEPTH_SHALLOW",
                            $"Section '{placement.CtdSection}' may be too coarse; consider a deeper CTD node."));
                    }

                    if (validationMode == ValidationMode.Strict && !sectionMatch.IsStandard)
                    {
                        issues.Add(new ValidationIssueDto(
                            "Warning",
                            "NON_STANDARD_SECTION_PATTERN",
                            $"Section '{placement.CtdSection}' is valid but uses a non-standard FDA/ICH segment pattern."));
                    }
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
        var report = new ValidationReportDto(request.ApplicationId, request.SequenceNumber, profileName, isValid, issues, sectionMatches, lifecycleMatches);

        await TryWriteAuditAsync(report, cancellationToken);
        return report;
    }

    private async Task TryWriteAuditAsync(ValidationReportDto report, CancellationToken cancellationToken)
    {
        try
        {
            var action = report.IsValid ? "ValidationPassed" : "ValidationFailed";
            var matchedPrefixes = report.SectionMatches
                .Where(x => !string.IsNullOrWhiteSpace(x.MatchedPrefix))
                .Select(x => x.MatchedPrefix!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var lifecycleResults = report.LifecycleMatches
                .GroupBy(x => x.ResultCode, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.Key}:{x.Count()}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var details = $"Profile={report.ValidationProfile}; Issue count: {report.Issues.Count}; MatchedPrefixes={(matchedPrefixes.Length == 0 ? "none" : string.Join(",", matchedPrefixes))}; LifecycleResults={(lifecycleResults.Length == 0 ? "none" : string.Join(",", lifecycleResults))}";

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

    private static bool IsSupportedOperation(DocumentPlacementOperation operation)
    {
        return operation is DocumentPlacementOperation.New
            or DocumentPlacementOperation.Replace
            or DocumentPlacementOperation.Delete
            or DocumentPlacementOperation.Append;
    }

    private static int CompareSequenceNumbers(string left, string right)
    {
        if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static string? GuessMediaTypeByFileName(string fileName)
    {
        return EctdDocumentFileRules.TryGetMediaType(fileName);
    }

    private static string BuildLifecycleErrorMessage(DocumentPlacement placement, LifecycleTargetResolution resolution)
    {
        return resolution.ResultCode switch
        {
            "LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE" => $"A lifecycle target for {placement.Operation} exists only in the current sequence for section {placement.CtdSection} and document {placement.DocumentId}.",
            "LIFECYCLE_TARGET_AMBIGUOUS" => $"Multiple historical targets were found for {placement.Operation} in section {placement.CtdSection} for document {placement.DocumentId}.",
            _ => $"No historical target was found for {placement.Operation} in section {placement.CtdSection} for document {placement.DocumentId}."
        };
    }
}
