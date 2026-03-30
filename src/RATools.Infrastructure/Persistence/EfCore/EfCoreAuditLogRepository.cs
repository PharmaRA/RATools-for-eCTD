using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Auditing;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreAuditLogRepository(RAToolsDbContext dbContext) : IAuditLogRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        await dbContext.AuditLogs.AddAsync(entry.ToRecord(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AuditLogEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await dbContext.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }
}

internal static class AuditLogRecordMapping
{
    public static AuditLogRecord ToRecord(this AuditLogEntry entry)
    {
        return new AuditLogRecord
        {
            Id = entry.Id,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            Actor = entry.Actor,
            Details = entry.Details,
            CreatedUtc = entry.CreatedUtc
        };
    }

    public static AuditLogEntry ToDomain(this AuditLogRecord record)
    {
        return AuditLogEntry.Rehydrate(
            record.Id,
            record.EntityType,
            record.EntityId,
            record.Action,
            record.Actor,
            record.Details,
            record.CreatedUtc);
    }
}
