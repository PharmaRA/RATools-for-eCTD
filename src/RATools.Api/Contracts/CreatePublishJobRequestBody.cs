using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class CreatePublishJobRequestBody
{
    [Required]
    public Guid ApplicationId { get; init; }

    [Required]
    [RegularExpression("^\\d{4}$")]
    public string SequenceNumber { get; init; } = string.Empty;

    // Optional explicit reference to a completed independent validation run. The
    // publish service verifies ownership, sequence and an archived report.
    public Guid? PackageValidationRunId { get; init; }
}
