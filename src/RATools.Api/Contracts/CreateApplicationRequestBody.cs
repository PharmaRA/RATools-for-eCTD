using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class CreateApplicationRequestBody
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    public string ApplicationNumber { get; init; } = string.Empty;

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Region { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string SponsorName { get; init; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string WorkingDirectoryParentPath { get; init; } = string.Empty;
}
