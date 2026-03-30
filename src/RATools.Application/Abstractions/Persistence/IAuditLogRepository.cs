using RATools.Domain.Auditing;

namespace RATools.Application.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AuditLogEntry>> ListAsync(CancellationToken cancellationToken = default);
}
