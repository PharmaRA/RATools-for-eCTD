namespace RATools.Application.Auditing.Requests;

public sealed record CreateAuditLogRequest(
    string EntityType,
    string EntityId,
    string Action,
    string? Details);
