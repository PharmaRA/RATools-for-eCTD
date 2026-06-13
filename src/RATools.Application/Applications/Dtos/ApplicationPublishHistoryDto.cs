using RATools.Application.Publishing.Dtos;
using RATools.Application.Validation.Dtos;

namespace RATools.Application.Applications.Dtos;

public sealed record ApplicationPublishHistoryDto(
    Guid ApplicationId,
    string ApplicationNumber,
    string SponsorName,
    int Page,
    int PageSize,
    int TotalCount,
    ApplicationPublishHistoryStatusSummaryDto StatusSummary,
    ApplicationPublishHistoryReadinessAggregateDto ReadinessSummary,
    ApplicationPublishHistoryLifecycleSummaryDto LifecycleSummary,
    IReadOnlyCollection<ApplicationPublishHistoryEntryDto> Entries);

public sealed record ApplicationPublishHistoryStatusSummaryDto(
    int CompletedCount,
    int FailedCount,
    int RunningCount);

public sealed record ApplicationPublishHistoryLifecycleSummaryDto(
    int MatchedCount,
    int ReplaceTargetNotFoundCount,
    int DeleteTargetNotFoundCount,
    int AppendTargetNotFoundCount,
    int AmbiguousCount,
    int CurrentSequenceCount);

public sealed record ApplicationPublishHistoryEntryDto(
    Guid PublishJobId,
    string SequenceNumber,
    string Status,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    bool ReportAvailable,
    bool ReportReadable,
    string? ReportError,
    string? ValidationProfile,
    int? ErrorCount,
    int? WarningCount,
    string? WarningSummary,
    ApplicationPublishHistoryReadinessSummaryDto? PublishReadiness,
    ApplicationPublishHistoryLifecycleSummaryDto LifecycleSummary,
    IReadOnlyCollection<ValidationLifecycleMatchDto> LifecycleMatches,
    PublishArtifactSummaryDto? ArtifactSummary,
    string? ReportPath,
    string? PackagePath);

public sealed record ApplicationPublishHistoryReadinessSummaryDto(
    bool IsReady,
    string Status,
    int BlockingErrorCount,
    int WarningCount,
    IReadOnlyCollection<string> MissingMetadataFields);

public sealed record ApplicationPublishHistoryReadinessAggregateDto(
    int ReadyCount,
    int BlockedCount,
    int UnknownCount);
