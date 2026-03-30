using RATools.Domain.Common;

namespace RATools.Domain.Auditing;

public sealed class AuditLogEntry : Entity
{
    public AuditLogEntry(string entityType, string entityId, string action, string actor)
        : this(Guid.NewGuid(), entityType, entityId, action, actor, null, DateTime.UtcNow)
    {
    }

    private AuditLogEntry(
        Guid id,
        string entityType,
        string entityId,
        string action,
        string actor,
        string? details,
        DateTime createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        Id = id;
        EntityType = entityType.Trim();
        EntityId = entityId.Trim();
        Action = action.Trim();
        Actor = actor.Trim();
        Details = details?.Trim();
        CreatedUtc = createdUtc;
    }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    public string Action { get; private set; }

    public string Actor { get; private set; }

    public string? Details { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public void AddDetails(string? details)
    {
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
    }

    public static AuditLogEntry Rehydrate(
        Guid id,
        string entityType,
        string entityId,
        string action,
        string actor,
        string? details,
        DateTime createdUtc)
    {
        return new AuditLogEntry(id, entityType, entityId, action, actor, details, createdUtc);
    }
}
