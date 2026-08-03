namespace RATools.Application.Auditing.Dtos;

/// <summary>
/// 审计日志分页结果。形状与 <c>ApplicationPublishHistoryDto</c> 的分页头部一致
/// （Page / PageSize / TotalCount + 条目集合），前端分页组件可沿用同一套约定。
/// </summary>
public sealed record AuditLogPageDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyCollection<AuditLogDto> Items);
