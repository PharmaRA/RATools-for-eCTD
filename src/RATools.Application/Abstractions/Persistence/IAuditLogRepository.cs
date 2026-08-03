using RATools.Application.Auditing;
using RATools.Domain.Auditing;

namespace RATools.Application.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页 + 过滤查询。过滤组合刻意落在 audit_logs 现有的两条索引上
    /// （CreatedUtc desc 供排序/分页，(EntityType, EntityId) 供实体过滤）。
    /// 返回当前页条目与满足过滤条件的总数（总数用于前端分页器）。
    /// </summary>
    Task<(IReadOnlyCollection<AuditLogEntry> Items, int TotalCount)> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 (EntityType, EntityId) 精确过滤。发布报告的审计摘要只关心当前作业与
    /// 当前序列的少量条目，全表拉取会随只增的审计表线性劣化。
    /// </summary>
    Task<IReadOnlyCollection<AuditLogEntry>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default);
}
