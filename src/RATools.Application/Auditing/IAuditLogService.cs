using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;

namespace RATools.Application.Auditing;

public interface IAuditLogService
{
    Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default);
}
