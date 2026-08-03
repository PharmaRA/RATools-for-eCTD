namespace RATools.Application.Auditing;

/// <summary>
/// 审计日志分页查询。过滤组合刻意落在 audit_logs 现有的两条索引上：
/// CreatedUtc desc（排序与时间范围）与 (EntityType, EntityId)（实体定位）。
/// </summary>
public sealed record AuditLogQuery(
    int Page = 1,
    int PageSize = 20,
    string? EntityType = null,
    string? EntityId = null,
    string? Action = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null);
