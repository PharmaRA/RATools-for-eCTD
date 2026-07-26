using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
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
