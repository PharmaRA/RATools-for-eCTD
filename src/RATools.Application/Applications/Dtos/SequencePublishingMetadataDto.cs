namespace RATools.Application.Applications.Dtos;

public sealed record SequencePublishingMetadataDto(
    Guid ApplicationId,
    string SequenceNumber,
    string StandardsProfile,
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
