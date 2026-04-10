using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Applications.Dtos;

public sealed record ApplicationPublishHistoryDto(
    Guid ApplicationId,
    string ApplicationNumber,
    string Region,
    string SponsorName,
    int Page,
    int PageSize,
    int TotalCount,
    ApplicationPublishHistoryStatusSummaryDto StatusSummary,
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
    ApplicationPublishHistoryLifecycleSummaryDto LifecycleSummary,
    PublishArtifactSummaryDto? ArtifactSummary,
    string? ReportPath,
    string? PackagePath);
