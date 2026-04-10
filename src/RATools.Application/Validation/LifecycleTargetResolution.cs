namespace RATools.Application.Validation;

public sealed record LifecycleTargetResolution(
    string ResultCode,
    string MatchStrategy,
    IReadOnlyCollection<string> AttemptedStrategies,
    int HistoricalMatchCount,
    IReadOnlyCollection<string> HistoricalSequenceNumbers,
    IReadOnlyCollection<Guid> HistoricalPlacementIds,
    string HistoricalFinalState);
