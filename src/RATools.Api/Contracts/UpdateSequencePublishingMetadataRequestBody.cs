using System.Text.Json.Serialization;
using RATools.Application.RegionalMetadata;

namespace RATools.Api.Contracts;

[JsonConverter(typeof(StrictRegionalRequestConverter<UpdateSequencePublishingMetadataRequestBody>))]
public sealed class UpdateSequencePublishingMetadataRequestBody
{
    public RegionalMetadataUpdate? Regional { get; set; }
    public long? ExpectedRevision { get; init; }

    public string? ApplicationType { get; set; }

    public string SubmissionType { get; set; } = string.Empty;

    public string? SubmissionSubtype { get; set; }

    public string SequenceDescription { get; set; } = string.Empty;

    public string ApplicantName { get; set; } = string.Empty;

    public string? FormType { get; set; }

    public string? ApplicantContactName { get; set; }

    public string? ApplicantContactType { get; set; }

    public string? Telephone { get; set; }

    public string? TelephoneNumberType { get; set; }

    public string? Email { get; set; }
}
