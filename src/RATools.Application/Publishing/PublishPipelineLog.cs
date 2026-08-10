using Microsoft.Extensions.Logging;

namespace RATools.Application.Publishing;

/// <summary>
/// 发布流水线的 LoggerMessage 定义（source generator，规避 CA1848 的装箱/格式化开销）。
/// 发布是本系统核心业务路径，此前全程零日志，生产事故只能靠数据库里的 FailureReason 排查。
/// </summary>
internal static partial class PublishPipelineLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Publish job {JobId} created for application {ApplicationId}, sequence {SequenceNumber}.")]
    public static partial void JobCreated(ILogger logger, Guid jobId, Guid applicationId, string sequenceNumber);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Publish job {JobId} stopped: sequence validation failed with {ErrorCount} error(s).")]
    public static partial void ValidationFailed(ILogger logger, Guid jobId, int errorCount);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Publish job {JobId} stopped: readiness check blocked with {BlockingErrorCount} blocking finding(s).")]
    public static partial void ReadinessBlocked(ILogger logger, Guid jobId, int blockingErrorCount);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Publish job {JobId} started backbone generation.")]
    public static partial void ExecutionStarted(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Publish job {JobId} completed. Output={OutputPath}; Package={PackagePath}.")]
    public static partial void Completed(ILogger logger, Guid jobId, string outputPath, string packagePath);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error,
        Message = "Publish job {JobId} failed during execution.")]
    public static partial void ExecutionFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning,
        Message = "Audit write failed for {EntityType} {EntityId}, action {Action}; publish execution continues.")]
    public static partial void AuditWriteFailed(ILogger logger, Exception exception, string entityType, string entityId, string action);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning,
        Message = "Building the audit summary for publish job {JobId} failed; the report will omit it.")]
    public static partial void AuditSummaryFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning,
        Message = "Audit write failed for sequence validation of application {ApplicationId}, sequence {SequenceNumber}.")]
    public static partial void ValidationAuditWriteFailed(ILogger logger, Exception exception, Guid applicationId, string sequenceNumber);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning,
        Message = "Persisting terminal state {Status} for publish job {JobId} failed; retrying once with a fresh cleanup token.")]
    public static partial void TerminalPersistenceRetry(
        ILogger logger,
        Exception exception,
        Guid jobId,
        string status);
}
