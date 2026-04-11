using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreDocumentRepository(RAToolsDbContext dbContext) : IDocumentRepository
{
    public async Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
    {
        await dbContext.Documents.AddAsync(document.ToRecord(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Documents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        dbContext.Documents.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return record?.ToDomain();
    }

    public async Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await dbContext.Documents
            .AsNoTracking()
            .OrderBy(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }
}

internal static class DocumentRecordMapping
{
    public static DocumentRecord ToRecord(this SubmissionDocument document)
    {
        return new DocumentRecord
        {
            Id = document.Id,
            FileName = document.FileName,
            MediaType = document.MediaType,
            FileSize = document.FileSize,
            Sha256 = document.Sha256,
            StoragePath = document.StoragePath,
            CreatedUtc = document.CreatedUtc
        };
    }

    public static SubmissionDocument ToDomain(this DocumentRecord record)
    {
        return SubmissionDocument.Rehydrate(
            record.Id,
            record.FileName,
            record.MediaType,
            record.FileSize,
            record.Sha256,
            record.StoragePath,
            record.CreatedUtc);
    }
}
