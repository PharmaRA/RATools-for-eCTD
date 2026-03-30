using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class CreateDocumentPlacementRequestBody
{
    [Required]
    public Guid DocumentId { get; init; }

    [Required]
    public Guid ApplicationId { get; init; }

    [Required]
    [RegularExpression("^\\d{4}$")]
    public string SequenceNumber { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string CtdSection { get; init; } = string.Empty;

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Operation { get; init; } = string.Empty;

    [StringLength(512)]
    public string? Title { get; init; }
}
