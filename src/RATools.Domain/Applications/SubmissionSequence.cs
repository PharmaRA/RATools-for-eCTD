namespace RATools.Domain.Applications;

public sealed class SubmissionSequence
{
    public SubmissionSequence(string sequenceNumber, string submissionType, string description)
        : this(sequenceNumber, submissionType, description, DateTime.UtcNow)
    {
    }

    private SubmissionSequence(string sequenceNumber, string submissionType, string description, DateTime createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        SequenceNumber = sequenceNumber.Trim();
        SubmissionType = submissionType.Trim();
        Description = description.Trim();
        CreatedUtc = createdUtc;
    }

    public static SubmissionSequence Rehydrate(string sequenceNumber, string submissionType, string description, DateTime createdUtc)
    {
        return new SubmissionSequence(sequenceNumber, submissionType, description, createdUtc);
    }

    public string SequenceNumber { get; }

    public string SubmissionType { get; }

    public string Description { get; }

    public DateTime CreatedUtc { get; }
}
