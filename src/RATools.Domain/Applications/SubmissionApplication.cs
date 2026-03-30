using RATools.Domain.Common;

namespace RATools.Domain.Applications;

public sealed class SubmissionApplication : Entity
{
    private readonly List<SubmissionSequence> _sequences = [];

    public SubmissionApplication(string applicationNumber, string region, string sponsorName)
        : this(Guid.NewGuid(), applicationNumber, region, sponsorName, DateTime.UtcNow, [])
    {
    }

    private SubmissionApplication(
        Guid id,
        string applicationNumber,
        string region,
        string sponsorName,
        DateTime createdUtc,
        IEnumerable<SubmissionSequence> sequences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(sponsorName);

        Id = id;
        ApplicationNumber = applicationNumber.Trim();
        Region = region.Trim();
        SponsorName = sponsorName.Trim();
        CreatedUtc = createdUtc;
        _sequences.AddRange(sequences);
    }

    public string ApplicationNumber { get; private set; }

    public string Region { get; private set; }

    public string SponsorName { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public IReadOnlyCollection<SubmissionSequence> Sequences => _sequences.AsReadOnly();

    public SubmissionSequence CreateSequence(string sequenceNumber, string submissionType, string description)
    {
        if (_sequences.Any(x => x.SequenceNumber == sequenceNumber))
        {
            throw new InvalidOperationException($"Sequence {sequenceNumber} already exists.");
        }

        var sequence = new SubmissionSequence(sequenceNumber, submissionType, description);
        _sequences.Add(sequence);

        return sequence;
    }

    public static SubmissionApplication Rehydrate(
        Guid id,
        string applicationNumber,
        string region,
        string sponsorName,
        DateTime createdUtc,
        IEnumerable<SubmissionSequence> sequences)
    {
        return new SubmissionApplication(id, applicationNumber, region, sponsorName, createdUtc, sequences);
    }
}
