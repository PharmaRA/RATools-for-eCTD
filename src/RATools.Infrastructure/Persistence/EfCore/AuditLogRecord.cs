namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class AuditLogRecord
{
    public Guid Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string? Details { get; set; }

    public DateTime CreatedUtc { get; set; }
}
