using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class CreateDocumentRequestBody
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string FileName { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 3)]
    public string MediaType { get; init; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long FileSize { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 16)]
    public string Sha256 { get; init; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string StoragePath { get; init; } = string.Empty;
}
