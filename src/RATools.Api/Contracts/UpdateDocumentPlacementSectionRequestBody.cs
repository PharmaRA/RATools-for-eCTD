using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class UpdateDocumentPlacementSectionRequestBody
{
    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string CtdSection { get; init; } = string.Empty;
}
