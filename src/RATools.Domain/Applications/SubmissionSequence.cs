namespace RATools.Domain.Applications;

public sealed class SubmissionSequence
{
    public SubmissionSequence(string sequenceNumber, string submissionType, string description)
        : this(sequenceNumber, submissionType, description, DateTime.UtcNow, null)
    {
    }

    private SubmissionSequence(
        string sequenceNumber,
        string submissionType,
        string description,
        DateTime createdUtc,
        SequencePublishingMetadata? publishingMetadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        SequenceNumber = sequenceNumber.Trim();
        SubmissionType = submissionType.Trim();
        Description = description.Trim();
        CreatedUtc = createdUtc;
        PublishingMetadata = publishingMetadata;
    }

    public static SubmissionSequence Rehydrate(
        string sequenceNumber,
        string submissionType,
        string description,
        DateTime createdUtc,
        SequencePublishingMetadata? publishingMetadata = null)
    {
        return new SubmissionSequence(sequenceNumber, submissionType, description, createdUtc, publishingMetadata);
    }

    public string SequenceNumber { get; }

    public string SubmissionType { get; }

    public string Description { get; }

    public DateTime CreatedUtc { get; }

    public SequencePublishingMetadata? PublishingMetadata { get; private set; }

    public void RevisePublishingMetadata(SequencePublishingMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        PublishingMetadata = metadata;
    }
}
