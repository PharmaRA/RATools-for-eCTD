namespace RATools.Application.Validation.Dtos;

public sealed record ValidationLifecycleMatchDto(
    string Operation,
    string SequenceNumber,
    string CtdSection,
    Guid DocumentId,
    string ResultCode,
    string MatchStrategy,
    int HistoricalMatchCount,
    IReadOnlyCollection<string> HistoricalSequenceNumbers);
