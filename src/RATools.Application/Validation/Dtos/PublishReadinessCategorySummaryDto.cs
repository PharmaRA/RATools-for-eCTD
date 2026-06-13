namespace RATools.Application.Validation.Dtos;

public sealed record PublishReadinessCategorySummaryDto(
    string Category,
    int BlockingErrorCount,
    int WarningCount,
    int FindingCount);
