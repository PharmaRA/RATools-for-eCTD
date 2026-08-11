using Microsoft.Extensions.Logging;
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
    IValidationProfileProvider validationProfileProvider,
    ILogger<SequenceValidationService> logger,
    IDocumentStorageBoundary documentStorageBoundary) : ISequenceValidationService
{
    public async Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssueDto>();
        var sectionMatches = new List<ValidationSectionMatchDto>();
        var lifecycleMatches = new List<ValidationLifecycleMatchDto>();
        var validationMode = validationProfileProvider.Mode;

        static ValidationIssueDto PlacementIssue(string severity, string code, string message, DocumentPlacement placement)
            => new(severity, code, message, placement.CtdSection, placement.DocumentId, placement.Id);

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

        // 官方验证准则：序列号必须是 4 位数字（此前唯一的格式检查埋在 import 侧）。
        if (!IsFourDigitSequenceNumber(request.SequenceNumber))
        {
            issues.Add(new ValidationIssueDto(
                "Error",
                "SEQUENCE_NUMBER_FORMAT_INVALID",
                $"Sequence number '{request.SequenceNumber}' is not a four-digit eCTD sequence number."));
        }

        // 序列号跳号提示：编号不连续通常意味着遗漏或删错序列，官方受理系统会问询。
        if (validationMode == ValidationMode.Strict)
        {
            var numericSequences = application.Sequences
                .Select(x => x.SequenceNumber)
                .Where(IsFourDigitSequenceNumber)
                .Select(int.Parse)
                .OrderBy(x => x)
                .ToArray();
            for (var index = 1; index < numericSequences.Length; index += 1)
            {
                if (numericSequences[index] - numericSequences[index - 1] > 1)
                {
                    issues.Add(new ValidationIssueDto(
                        "Warning",
                        "SEQUENCE_GAP_DETECTED",
                        $"Application sequences jump from {numericSequences[index - 1]:0000} to {numericSequences[index]:0000}; confirm the gap is intentional."));
                }
            }
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
                issues.Add(PlacementIssue(
                    "Warning",
                    "DUPLICATE_PLACEMENT",
                    $"Document {duplicate.Key.DocumentId} appears multiple times in section {duplicate.Key.Section}.",
                    duplicate.First()));
            }
        }

        var currentSequenceDocumentIds = placements.Select(x => x.DocumentId).ToHashSet();
        var referencedDocumentIds = applicationPlacements
            .Select(x => x.DocumentId)
            .Concat(currentSequenceDocumentIds)
            .Distinct()
            .ToArray();
        var documents = await documentRepository.ListByIdsPreferScopedAsync(referencedDocumentIds, cancellationToken);
        var documentById = documents
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());
        var invalidStoragePlacementIds = new HashSet<Guid>();

        foreach (var placement in placements)
        {
            if (!documentById.TryGetValue(placement.DocumentId, out var document))
            {
                continue;
            }

            try
            {
                documentStorageBoundary.EnsureDocumentOwnedBySequence(document, application, request.SequenceNumber);
            }
            catch (DocumentStorageBoundaryException)
            {
                invalidStoragePlacementIds.Add(placement.Id);
                issues.Add(PlacementIssue(
                    "Error",
                    "DOCUMENT_STORAGE_SCOPE_INVALID",
                    $"Document {document.Id} is outside the workspace owned by application {application.Id} sequence {request.SequenceNumber}.",
                    placement));
            }
        }

        var validCurrentDocumentIds = placements
            .Where(x => !invalidStoragePlacementIds.Contains(x.Id))
            .Select(x => x.DocumentId)
            .ToHashSet();
        var referencedDocuments = documents.Where(x => validCurrentDocumentIds.Contains(x.Id)).ToArray();

        var duplicatePublishedPaths = referencedDocuments
            .GroupBy(document => PublishOutputNaming.BuildPublishedDocumentRelativePath(document, request.SequenceNumber), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .ToArray();

        foreach (var duplicatePublishedPath in duplicatePublishedPaths)
        {
            var duplicateDocumentIds = duplicatePublishedPath.Select(x => x.Id).ToHashSet();
            var placement = placements.First(x => duplicateDocumentIds.Contains(x.DocumentId));
            issues.Add(PlacementIssue(
                "Error",
                "DUPLICATE_PUBLISHED_DOCUMENT_PATH",
                $"Multiple documents resolve to the same published path '{duplicatePublishedPath.Key}'.",
                placement));
        }

        foreach (var placement in placements)
        {
            if (!IsSupportedOperation(placement.Operation))
            {
                issues.Add(PlacementIssue(
                    "Error",
                    "UNSUPPORTED_OPERATION_VALUE",
                    $"Operation '{placement.Operation}' is not supported for backbone generation.",
                    placement));
                continue;
            }

            if (!documentById.TryGetValue(placement.DocumentId, out var document))
            {
                issues.Add(PlacementIssue(
                    "Error",
                    "DOCUMENT_NOT_FOUND",
                    $"Referenced document {placement.DocumentId} was not found for section {placement.CtdSection}.",
                    placement));
                continue;
            }

            if (invalidStoragePlacementIds.Contains(placement.Id))
            {
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
                    issues.Add(PlacementIssue(
                        "Error",
                        resolution.ResultCode,
                        BuildLifecycleErrorMessage(placement, resolution),
                        placement));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(document.FileName)
                || string.IsNullOrWhiteSpace(document.MediaType)
                || string.IsNullOrWhiteSpace(document.Sha256))
            {
                issues.Add(PlacementIssue(
                    "Error",
                    "MISSING_LEAF_CORE_METADATA",
                    $"Document {document.Id} is missing required backbone metadata (file name, media type, or checksum).",
                    placement));
                continue;
            }

            if (string.IsNullOrWhiteSpace(placement.CtdSection))
            {
                issues.Add(PlacementIssue(
                    "Error",
                    "SECTION_MISSING",
                    $"Document {document.FileName} is missing a CTD section.",
                    placement));
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
                    issues.Add(PlacementIssue(
                        "Error",
                        "INVALID_SECTION_PATH",
                        $"Section '{placement.CtdSection}' is not a valid CTD section path.",
                        placement));
                }
                else
                {
                    var sectionDepth = placement.CtdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    if (validationMode == ValidationMode.Strict && sectionDepth < 2)
                    {
                        issues.Add(PlacementIssue(
                            "Warning",
                            "SECTION_DEPTH_SHALLOW",
                            $"Section '{placement.CtdSection}' may be too coarse; consider a deeper CTD node.",
                            placement));
                    }

                    if (validationMode == ValidationMode.Strict && !sectionMatch.IsStandard)
                    {
                        issues.Add(PlacementIssue(
                            "Warning",
                            "NON_STANDARD_SECTION_PATTERN",
                            $"Section '{placement.CtdSection}' is valid but uses a non-standard FDA/ICH segment pattern.",
                            placement));
                    }
                }
            }

            if (validationMode == ValidationMode.Strict && string.IsNullOrWhiteSpace(placement.Title))
            {
                issues.Add(PlacementIssue(
                    "Warning",
                    "TITLE_FALLBACK_USED",
                    $"Placement for document {document.FileName} has no explicit title, so the file name will be used in the backbone.",
                    placement));
            }

            var expectedMediaType = GuessMediaTypeByFileName(document.FileName);
            if (validationMode == ValidationMode.Strict &&
                !string.IsNullOrWhiteSpace(expectedMediaType) &&
                !string.Equals(expectedMediaType, document.MediaType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(PlacementIssue(
                    "Warning",
                    "MEDIA_TYPE_MISMATCH",
                    $"Document {document.FileName} media type '{document.MediaType}' does not match expected '{expectedMediaType}'.",
                    placement));
            }

            if (!File.Exists(document.StoragePath))
            {
                issues.Add(PlacementIssue(
                    "Error",
                    "FILE_MISSING",
                    $"Document file '{document.StoragePath}' does not exist.",
                    placement));
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

            await auditLogService.WriteSystemEventAsync(
                new CreateAuditLogRequest(
                    "SequenceValidation",
                    $"{report.ApplicationId}:{report.SequenceNumber}",
                    action,
                    details),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 审计写入不阻断验证，但缺失必须留痕。
            PublishPipelineLog.ValidationAuditWriteFailed(logger, exception, report.ApplicationId, report.SequenceNumber);
        }
    }

    private static bool IsFourDigitSequenceNumber(string sequenceNumber)
        => sequenceNumber.Length == 4 && sequenceNumber.All(char.IsAsciiDigit);

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
            "LIFECYCLE_TARGET_INVALID" => $"The selected lifecycle target for {placement.Operation} in section {placement.CtdSection} is not a valid historical placement.",
            "LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE" => $"A lifecycle target for {placement.Operation} exists only in the current sequence for section {placement.CtdSection} and document {placement.DocumentId}.",
            "LIFECYCLE_TARGET_AMBIGUOUS" => $"Multiple historical targets were found for {placement.Operation} in section {placement.CtdSection} for document {placement.DocumentId}. Select the target placement explicitly.",
            "LIFECYCLE_TARGET_SUPERSEDED" => $"The lifecycle target for {placement.Operation} in section {placement.CtdSection} was already replaced by a later sequence. Target the latest active leaf instead.",
            "LIFECYCLE_TARGET_DELETED" => $"The lifecycle target for {placement.Operation} in section {placement.CtdSection} was already deleted by a later sequence and cannot be modified.",
            _ => $"No historical target was found for {placement.Operation} in section {placement.CtdSection} for document {placement.DocumentId}."
        };
    }
}
