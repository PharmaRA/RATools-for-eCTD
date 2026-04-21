using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class UpdateDocumentPlacementMetadataRequestBody
{
    public string? Title { get; init; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string FileNamePrefix { get; init; } = string.Empty;
}
