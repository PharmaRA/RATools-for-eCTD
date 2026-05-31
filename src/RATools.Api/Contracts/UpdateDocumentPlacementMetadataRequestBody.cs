using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class UpdateDocumentPlacementMetadataRequestBody
{
    public string? Title { get; init; }

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Operation { get; init; } = string.Empty;

    public Guid? LifecycleTargetPlacementId { get; init; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string FileNamePrefix { get; init; } = string.Empty;
}
