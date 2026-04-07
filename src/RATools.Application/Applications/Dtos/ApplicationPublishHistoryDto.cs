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
    IReadOnlyCollection<ApplicationPublishHistoryEntryDto> Entries);

public sealed record ApplicationPublishHistoryStatusSummaryDto(
    int CompletedCount,
    int FailedCount,
    int RunningCount);

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
    PublishArtifactSummaryDto? ArtifactSummary,
    string? ReportPath,
    string? PackagePath);
