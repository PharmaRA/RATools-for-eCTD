using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreDocumentPlacementRepository(RAToolsDbContext dbContext) : IDocumentPlacementRepository
{
    public async Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
    {
        await dbContext.DocumentPlacements.AddAsync(placement.ToRecord(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.DocumentPlacements.SingleOrDefaultAsync(x => x.Id == placement.Id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        existing.CtdSection = placement.CtdSection;
        existing.Operation = placement.Operation.ToString();
        existing.Title = placement.Title;
        existing.LifecycleTargetPlacementId = placement.LifecycleTargetPlacementId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.DocumentPlacements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        dbContext.DocumentPlacements.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.DocumentPlacements
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return record?.ToDomain();
    }

    public async Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await dbContext.DocumentPlacements
            .AsNoTracking()
            .OrderBy(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var records = await dbContext.DocumentPlacements
            .AsNoTracking()
            .Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var records = await dbContext.DocumentPlacements
            .AsNoTracking()
            .Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber)
            .OrderBy(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }
}

internal static class DocumentPlacementRecordMapping
{
    public static DocumentPlacementRecord ToRecord(this DocumentPlacement placement)
    {
        return new DocumentPlacementRecord
        {
            Id = placement.Id,
            DocumentId = placement.DocumentId,
            ApplicationId = placement.ApplicationId,
            SequenceNumber = placement.SequenceNumber,
            CtdSection = placement.CtdSection,
            Operation = placement.Operation.ToString(),
            Title = placement.Title,
            LifecycleTargetPlacementId = placement.LifecycleTargetPlacementId,
            CreatedUtc = placement.CreatedUtc
        };
    }

    public static DocumentPlacement ToDomain(this DocumentPlacementRecord record)
    {
        var operation = Enum.Parse<DocumentPlacementOperation>(record.Operation, ignoreCase: true);
        return DocumentPlacement.Rehydrate(
            record.Id,
            record.DocumentId,
            record.ApplicationId,
            record.SequenceNumber,
            record.CtdSection,
            operation,
            record.Title,
            record.LifecycleTargetPlacementId,
            record.CreatedUtc);
    }
}
