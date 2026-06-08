namespace RATools.Domain.Applications;

public sealed record SequencePublishingMetadata(
    string? ApplicationType,
    string SubmissionType,
    string? SubmissionSubtype,
    string SequenceDescription,
    string ApplicantName,
    string? FormType,
    string? ApplicantContactName,
    string? ApplicantContactType,
    string? Telephone,
    string? TelephoneNumberType,
    string? Email)
{
    public static SequencePublishingMetadata Create(
        string? applicationType,
        string submissionType,
        string? submissionSubtype,
        string sequenceDescription,
        string applicantName,
        string? formType,
        string? applicantContactName = null,
        string? applicantContactType = null,
        string? telephone = null,
        string? telephoneNumberType = null,
        string? email = null)
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
            NormalizeOptional(formType),
            NormalizeOptional(applicantContactName),
            NormalizeOptional(applicantContactType),
            NormalizeOptional(telephone),
            NormalizeOptional(telephoneNumberType),
            NormalizeOptional(email));
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
