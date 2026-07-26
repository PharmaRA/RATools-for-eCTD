using RATools.Domain.Auditing;

namespace RATools.Application.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 (EntityType, EntityId) 精确过滤。发布报告的审计摘要只关心当前作业与
    /// 当前序列的少量条目，全表拉取会随只增的审计表线性劣化。
    /// </summary>
    Task<IReadOnlyCollection<AuditLogEntry>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default);
}
