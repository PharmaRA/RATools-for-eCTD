using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCorePublishJobRepository(RAToolsDbContext dbContext) : IPublishJobRepository
{
    public async Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        await dbContext.PublishJobs.AddAsync(job.ToRecord(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.PublishJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return record?.ToDomain();
    }

    public async Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await dbContext.PublishJobs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }
}

internal static class PublishJobRecordMapping
{
    public static PublishJobRecord ToRecord(this PublishJob job)
    {
        return new PublishJobRecord
        {
            Id = job.Id,
            ApplicationId = job.ApplicationId,
            SequenceNumber = job.SequenceNumber,
            Status = job.Status.ToString(),
            OutputPath = job.OutputPath,
            CreatedUtc = job.CreatedUtc,
            CompletedUtc = job.CompletedUtc,
            FailureReason = job.FailureReason
        };
    }

    public static PublishJob ToDomain(this PublishJobRecord record)
    {
        var status = Enum.Parse<PublishJobStatus>(record.Status, ignoreCase: true);
        return PublishJob.Rehydrate(
            record.Id,
            record.ApplicationId,
            record.SequenceNumber,
            status,
            record.OutputPath,
            record.CreatedUtc,
            record.CompletedUtc,
            record.FailureReason);
    }
}
