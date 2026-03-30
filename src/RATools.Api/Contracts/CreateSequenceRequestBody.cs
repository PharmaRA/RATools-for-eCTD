using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class CreateSequenceRequestBody
{
    [Required]
    [RegularExpression("^\\d{4}$")]
    public string SequenceNumber { get; init; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string SubmissionType { get; init; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 2)]
    public string Description { get; init; } = string.Empty;
}
