namespace RATools.Application.Auditing.Dtos;

public sealed record AuditLogDto(
    Guid Id,
    string EntityType,
    string EntityId,
    string Action,
    string Actor,
    string? Details,
    DateTime CreatedUtc);
