namespace RATools.Application.Validation.Dtos;

public sealed record PublishReadinessReportDto(
    Guid ApplicationId,
    string SequenceNumber,
    bool IsReady,
    string Status,
    int BlockingErrorCount,
    int WarningCount,
    ValidationReportDto ValidationReport,
    IReadOnlyCollection<PublishReadinessCategorySummaryDto> CategorySummaries,
    IReadOnlyCollection<PublishReadinessFindingDto> Findings);
