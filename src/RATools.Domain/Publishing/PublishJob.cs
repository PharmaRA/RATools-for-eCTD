using RATools.Domain.Common;

namespace RATools.Domain.Publishing;

public sealed class PublishJob : Entity
{
    public PublishJob(Guid applicationId, string sequenceNumber)
        : this(Guid.NewGuid(), applicationId, sequenceNumber, PublishJobStatus.Pending, null, DateTime.UtcNow, null, null)
    {
    }

    private PublishJob(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        PublishJobStatus status,
        string? outputPath,
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
        CreatedUtc = createdUtc;
        CompletedUtc = completedUtc;
        FailureReason = failureReason?.Trim();
    }

    public Guid ApplicationId { get; private set; }

    public string SequenceNumber { get; private set; }

    public PublishJobStatus Status { get; private set; }

    public string? OutputPath { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime? CompletedUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public void MarkRunning()
    {
        Status = PublishJobStatus.Running;
        FailureReason = null;
    }

    public void MarkCompleted(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        Status = PublishJobStatus.Completed;
        OutputPath = outputPath.Trim();
        CompletedUtc = DateTime.UtcNow;
        FailureReason = null;
    }

    public void MarkFailed(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        Status = PublishJobStatus.Failed;
        FailureReason = failureReason.Trim();
        CompletedUtc = DateTime.UtcNow;
    }

    public static PublishJob Rehydrate(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        PublishJobStatus status,
        string? outputPath,
        DateTime createdUtc,
        DateTime? completedUtc,
        string? failureReason)
    {
        return new PublishJob(id, applicationId, sequenceNumber, status, outputPath, createdUtc, completedUtc, failureReason);
    }
}
