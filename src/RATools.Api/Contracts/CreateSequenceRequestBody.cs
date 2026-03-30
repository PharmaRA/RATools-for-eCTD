using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed record CreateSequenceRequestBody(
    [property: Required]
    [property: RegularExpression("^\\d{4}$")]
    string SequenceNumber,

    [property: Required]
    [property: StringLength(64, MinimumLength = 2)]
    string SubmissionType,

    [property: Required]
    [property: StringLength(512, MinimumLength = 2)]
    string Description);
