using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Domain.Auditing;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryAuditLogRepository : IAuditLogRepository
{
    private readonly ConcurrentDictionary<Guid, AuditLogEntry> _items = new();

    public Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        _items[entry.Id] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AuditLogEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<AuditLogEntry> items = _items.Values
            .OrderByDescending(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }

    public Task<(IReadOnlyCollection<AuditLogEntry> Items, int TotalCount)> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var filtered = _items.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            filtered = filtered.Where(x => string.Equals(x.EntityType, query.EntityType, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            filtered = filtered.Where(x => string.Equals(x.EntityId, query.EntityId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            filtered = filtered.Where(x => string.Equals(x.Action, query.Action, StringComparison.Ordinal));
        }

        if (query.CreatedFromUtc is { } from)
        {
            filtered = filtered.Where(x => x.CreatedUtc >= from);
        }

        if (query.CreatedToUtc is { } to)
        {
            filtered = filtered.Where(x => x.CreatedUtc <= to);
        }

        var ordered = filtered.OrderByDescending(x => x.CreatedUtc).ToArray();
        var page = query.Page < 1 ? 1 : query.Page;
        IReadOnlyCollection<AuditLogEntry> items = ordered
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();

        return Task.FromResult((items, ordered.Length));
    }

    public Task<IReadOnlyCollection<AuditLogEntry>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        var wanted = entities.ToHashSet();
        IReadOnlyCollection<AuditLogEntry> items = _items.Values
            .Where(x => wanted.Contains((x.EntityType, x.EntityId)))
            .OrderByDescending(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(items);
    }
}
