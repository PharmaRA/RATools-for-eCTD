using System.ComponentModel.DataAnnotations;

namespace RATools.Api.Contracts;

public sealed class CreateAuditLogRequestBody
{
    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string EntityType { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string EntityId { get; init; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string Action { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string Actor { get; init; } = string.Empty;

    [StringLength(2048)]
    public string? Details { get; init; }
}
