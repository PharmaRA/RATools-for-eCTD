using RATools.Domain.Common;

namespace RATools.Domain.Publishing;

public sealed class PublishJob : Entity
{
    public PublishJob(Guid applicationId, string sequenceNumber)
        : this(Guid.NewGuid(), applicationId, sequenceNumber, PublishJobStatus.Pending, null, null, DateTime.UtcNow, null, null)
    {
    }

    private PublishJob(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        PublishJobStatus status,
        string? outputPath,
        string? packagePath,
        DateTime createdUtc,
        DateTime? completedUtc,
        string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        Id = id;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber.Trim();
        Status = status;
        OutputPath = outputPath?.Trim();
        PackagePath = packagePath?.Trim();
        CreatedUtc = createdUtc;
        CompletedUtc = completedUtc;
        FailureReason = failureReason?.Trim();
    }

    public Guid ApplicationId { get; private set; }

    public string SequenceNumber { get; private set; }

    public PublishJobStatus Status { get; private set; }

    public string? OutputPath { get; private set; }

    public string? PackagePath { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime? CompletedUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public void MarkRunning()
    {
        Status = PublishJobStatus.Running;
        FailureReason = null;
    }

    public void MarkCompleted(string outputPath, string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        Status = PublishJobStatus.Completed;
        OutputPath = outputPath.Trim();
        PackagePath = packagePath.Trim();
        CompletedUtc = DateTime.UtcNow;
        FailureReason = null;
    }

    public void MarkFailed(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        Status = PublishJobStatus.Failed;
        FailureReason = failureReason.Trim();
        PackagePath = null;
        CompletedUtc = DateTime.UtcNow;
    }

    public static PublishJob Rehydrate(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        PublishJobStatus status,
        string? outputPath,
        string? packagePath,
        DateTime createdUtc,
        DateTime? completedUtc,
        string? failureReason)
    {
        return new PublishJob(id, applicationId, sequenceNumber, status, outputPath, packagePath, createdUtc, completedUtc, failureReason);
    }
}
