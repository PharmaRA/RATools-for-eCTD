namespace RATools.Application.Applications.Requests;

public sealed record UpdateSequencePublishingMetadataRequest(
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
    string? Email);
