namespace RATools.Application.Abstractions.Persistence;

public sealed record PublishJobHistorySummary(
    bool ReportAvailable,
    bool ReportReadable,
    string? ReportError,
    string? ValidationProfile,
    int? ErrorCount,
    int? WarningCount,
    string? WarningSummary,
    bool? ReadinessIsReady,
    string? ReadinessStatus,
    int? ReadinessBlockingErrorCount,
    int? ReadinessWarningCount,
    IReadOnlyCollection<string> ReadinessMissingMetadataFields,
    int LifecycleMatchedCount,
    int LifecycleReplaceTargetNotFoundCount,
    int LifecycleDeleteTargetNotFoundCount,
    int LifecycleAppendTargetNotFoundCount,
    int LifecycleAmbiguousCount,
    int LifecycleCurrentSequenceCount,
    int? ArtifactFileCount,
    long? ArtifactTotalSizeBytes,
    long? ArtifactPackageSizeBytes,
    string? ReportPath);

public sealed record PublishJobHistoryReadinessCounts(
    int Ready,
    int Blocked,
    int Unknown);

public sealed record PublishJobHistoryLifecycleCounts(
    int Matched,
    int ReplaceTargetNotFound,
    int DeleteTargetNotFound,
    int AppendTargetNotFound,
    int Ambiguous,
    int CurrentSequence);
