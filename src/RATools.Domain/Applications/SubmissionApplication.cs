using RATools.Domain.Common;

namespace RATools.Domain.Applications;

public sealed class SubmissionApplication : Entity
{
    private readonly List<SubmissionSequence> _sequences = [];

    public SubmissionApplication(
        string applicationNumber,
        string region,
        string sponsorName,
        string workingDirectoryPath,
        string ectdTemplateKey)
        : this(Guid.NewGuid(), applicationNumber, region, sponsorName, DateTime.UtcNow, [], workingDirectoryPath, ectdTemplateKey)
    {
    }

    private SubmissionApplication(
        Guid id,
        string applicationNumber,
        string region,
        string sponsorName,
        DateTime createdUtc,
        IEnumerable<SubmissionSequence> sequences,
        string workingDirectoryPath,
        string ectdTemplateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(sponsorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ectdTemplateKey);

        Id = id;
        ApplicationNumber = PortablePathSegment.NormalizeAndValidate(applicationNumber, nameof(applicationNumber));
        Region = region.Trim();
        SponsorName = sponsorName.Trim();
        WorkingDirectoryPath = workingDirectoryPath.Trim();
        EctdTemplateKey = ectdTemplateKey.Trim();
        CreatedUtc = createdUtc;
        _sequences.AddRange(sequences);
    }

    public string ApplicationNumber { get; private set; }

    public string Region { get; private set; }

    public string SponsorName { get; private set; }

    public string EctdTemplateKey { get; private set; }

    public string WorkingDirectoryPath { get; private set; }

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

    public bool RemoveSequence(string sequenceNumber)
    {
        var sequence = _sequences.SingleOrDefault(x => x.SequenceNumber == sequenceNumber);
        if (sequence is null)
        {
            return false;
        }

        _sequences.Remove(sequence);
        return true;
    }

    public static SubmissionApplication Rehydrate(
        Guid id,
        string applicationNumber,
        string region,
        string sponsorName,
        DateTime createdUtc,
        IEnumerable<SubmissionSequence> sequences,
        string ectdTemplateKey)
    {
        return new SubmissionApplication(id, applicationNumber, region, sponsorName, createdUtc, sequences, $"workspace-{applicationNumber}", ectdTemplateKey);
    }

    public static SubmissionApplication Rehydrate(
        Guid id,
        string applicationNumber,
        string region,
        string sponsorName,
        DateTime createdUtc,
        IEnumerable<SubmissionSequence> sequences,
        string workingDirectoryPath,
        string ectdTemplateKey)
    {
        return new SubmissionApplication(id, applicationNumber, region, sponsorName, createdUtc, sequences, workingDirectoryPath, ectdTemplateKey);
    }
}
