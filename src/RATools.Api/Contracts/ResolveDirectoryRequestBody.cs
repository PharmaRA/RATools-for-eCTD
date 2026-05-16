using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class ResolveDirectoryRequestBody
{
    [Required]
    [StringLength(4096, MinimumLength = 1)]
    public string Path { get; init; } = string.Empty;
}
