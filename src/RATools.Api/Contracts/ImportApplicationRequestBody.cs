using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class ImportApplicationRequestBody
{
    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public string WorkingDirectoryPath { get; init; } = string.Empty;

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string EctdTemplateKey { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string SponsorName { get; init; } = string.Empty;
}
