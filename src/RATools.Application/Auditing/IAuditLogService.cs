using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;

namespace RATools.Application.Auditing;

public interface IAuditLogService
{
    Task<AuditLogDto> WriteSystemEventAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页 + 过滤查询。PageSize 由调用方（controller）clamp 后传入。
    /// </summary>
    Task<AuditLogPageDto> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default);
}
