using RATools.Domain.Common;

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
        SequencePublishingMetadata? publishingMetadata,
        long workspaceRevision = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        SequenceNumber = PortablePathSegment.NormalizeAndValidate(sequenceNumber, nameof(sequenceNumber));
        SubmissionType = submissionType.Trim();
        Description = description.Trim();
        CreatedUtc = createdUtc;
        PublishingMetadata = publishingMetadata;
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workspaceRevision, 9007199254740991L);
        WorkspaceRevision = workspaceRevision;
    }

    public static SubmissionSequence Rehydrate(
        string sequenceNumber,
        string submissionType,
        string description,
        DateTime createdUtc,
        SequencePublishingMetadata? publishingMetadata = null,
        long workspaceRevision = 0)
    {
        return new SubmissionSequence(sequenceNumber, submissionType, description, createdUtc, publishingMetadata, workspaceRevision);
    }

    public string SequenceNumber { get; }

    public long WorkspaceRevision { get; private set; }

    public void AdvanceWorkspaceRevision(long expectedRevision)
    {
        if (WorkspaceRevision != expectedRevision) throw new InvalidOperationException("The sequence revision has changed.");
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(expectedRevision, 9007199254740991L);
        WorkspaceRevision++;
    }

    public string SubmissionType { get; }

    public string Description { get; }

    public DateTime CreatedUtc { get; }

    public SequencePublishingMetadata? PublishingMetadata { get; private set; }

    public void RevisePublishingMetadata(SequencePublishingMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        PublishingMetadata = metadata;
    }

    public void RestorePublishingMetadata(SequencePublishingMetadata? metadata) => PublishingMetadata = metadata;
}
