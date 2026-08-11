namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class PublishJobRecord
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public string SequenceNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? OutputPath { get; set; }

    public string? PackagePath { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public string? FailureReason { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public DateTime NextAttemptUtc { get; set; }

    public string? LeaseOwner { get; set; }

    public Guid? LeaseToken { get; set; }

    public DateTime? LeaseExpiresUtc { get; set; }

    public DateTime? LastHeartbeatUtc { get; set; }

    public bool? HistoryReportAvailable { get; set; }

    public bool? HistoryReportReadable { get; set; }

    public string? HistoryReportError { get; set; }

    public string? HistoryValidationProfile { get; set; }

    public int? HistoryValidationErrorCount { get; set; }

    public int? HistoryValidationWarningCount { get; set; }

    public string? HistoryValidationWarningSummary { get; set; }

    public bool? HistoryReadinessIsReady { get; set; }

    public string? HistoryReadinessStatus { get; set; }

    public int? HistoryReadinessBlockingErrorCount { get; set; }

    public int? HistoryReadinessWarningCount { get; set; }

    public string? HistoryReadinessMissingMetadataFieldsJson { get; set; }

    public int? HistoryLifecycleMatchedCount { get; set; }

    public int? HistoryLifecycleReplaceTargetNotFoundCount { get; set; }

    public int? HistoryLifecycleDeleteTargetNotFoundCount { get; set; }

    public int? HistoryLifecycleAppendTargetNotFoundCount { get; set; }

    public int? HistoryLifecycleAmbiguousCount { get; set; }

    public int? HistoryLifecycleCurrentSequenceCount { get; set; }

    public int? HistoryArtifactFileCount { get; set; }

    public long? HistoryArtifactTotalSizeBytes { get; set; }

    public long? HistoryArtifactPackageSizeBytes { get; set; }

    public string? HistoryReportPath { get; set; }
}
