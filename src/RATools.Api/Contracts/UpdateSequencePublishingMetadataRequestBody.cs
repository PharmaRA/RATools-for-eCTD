namespace RATools.Api.Contracts;

public sealed class UpdateSequencePublishingMetadataRequestBody
{
    public string? ApplicationType { get; set; }

    public string SubmissionType { get; set; } = string.Empty;

    public string? SubmissionSubtype { get; set; }

    public string SequenceDescription { get; set; } = string.Empty;

    public string ApplicantName { get; set; } = string.Empty;

    public string? FormType { get; set; }
}
