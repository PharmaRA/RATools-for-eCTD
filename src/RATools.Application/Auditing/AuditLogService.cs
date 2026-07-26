using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Domain.Auditing;

namespace RATools.Application.Auditing;

public sealed class AuditLogService(IAuditLogRepository repository) : IAuditLogService
{
    public async Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(request.EntityType, request.EntityId, request.Action, request.Actor);
        entry.AddDetails(request.Details);

        await repository.AddAsync(entry, cancellationToken);
        return entry.ToDto();
    }

    public async Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await repository.ListAsync(cancellationToken);
        return items.Select(x => x.ToDto()).ToArray();
    }

    public async Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
        IReadOnlyCollection<(string EntityType, string EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        var items = await repository.ListByEntitiesAsync(entities, cancellationToken);
        return items.Select(x => x.ToDto()).ToArray();
    }
}

internal static class AuditLogMapping
{
    public static AuditLogDto ToDto(this AuditLogEntry entry)
    {
        return new AuditLogDto(
            entry.Id,
            entry.EntityType,
            entry.EntityId,
            entry.Action,
            entry.Actor,
            entry.Details,
            entry.CreatedUtc);
    }
}
