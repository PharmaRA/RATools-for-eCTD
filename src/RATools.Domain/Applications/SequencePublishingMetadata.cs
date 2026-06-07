namespace RATools.Domain.Applications;

public sealed record SequencePublishingMetadata(
    string? ApplicationType,
    string SubmissionType,
    string? SubmissionSubtype,
    string SequenceDescription,
    string ApplicantName,
    string? FormType)
{
    public static SequencePublishingMetadata Create(
        string? applicationType,
        string submissionType,
        string? submissionSubtype,
        string sequenceDescription,
        string applicantName,
        string? formType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicantName);

        return new SequencePublishingMetadata(
            NormalizeOptional(applicationType),
            submissionType.Trim(),
            NormalizeOptional(submissionSubtype),
            sequenceDescription.Trim(),
            applicantName.Trim(),
            NormalizeOptional(formType));
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
