using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
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

    public async Task<(IReadOnlyCollection<AuditLogEntry> Items, int TotalCount)> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilters(dbContext.AuditLogs.AsNoTracking(), query);

        // 先算总数再取页：分页元数据必须反映过滤后的全集，而不是当前页。
        var totalCount = await filtered.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var records = await filtered
            .OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return (records.Select(x => x.ToDomain()).ToArray(), totalCount);
    }

    private static IQueryable<AuditLogRecord> ApplyFilters(IQueryable<AuditLogRecord> source, AuditLogQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            source = source.Where(x => x.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            source = source.Where(x => x.EntityId == query.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            source = source.Where(x => x.Action == query.Action);
        }

        if (query.CreatedFromUtc is { } from)
        {
            source = source.Where(x => x.CreatedUtc >= from);
        }

        if (query.CreatedToUtc is { } to)
        {
            source = source.Where(x => x.CreatedUtc <= to);
        }

        return source;
    }

    public async Task<IReadOnlyCollection<AuditLogEntry>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
        {
            return [];
        }

        // 组合键无法直接翻译成 SQL IN；按 EntityId 走索引取候选，再在内存中核对配对。
        var entityIds = entities.Select(x => x.EntityId).Distinct(StringComparer.Ordinal).ToArray();
        var candidates = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(x => entityIds.Contains(x.EntityId))
            .OrderByDescending(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        var wanted = entities.ToHashSet();
        return candidates
            .Where(x => wanted.Contains((x.EntityType, x.EntityId)))
            .Select(x => x.ToDomain())
            .ToArray();
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
