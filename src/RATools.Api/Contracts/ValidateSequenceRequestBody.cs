using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class ValidateSequenceRequestBody
{
    [Required]
    public Guid ApplicationId { get; init; }

    [Required]
    [RegularExpression("^\\d{4}$")]
    public string SequenceNumber { get; init; } = string.Empty;
}
