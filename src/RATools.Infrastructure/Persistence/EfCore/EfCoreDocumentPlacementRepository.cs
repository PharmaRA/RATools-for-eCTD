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
            record.CreatedUtc);
    }
}
