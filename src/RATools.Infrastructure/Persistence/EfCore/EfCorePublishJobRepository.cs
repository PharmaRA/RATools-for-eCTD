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

    public async Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.PublishJobs.SingleOrDefaultAsync(x => x.Id == job.Id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.Status = job.Status.ToString();
        existing.OutputPath = job.OutputPath;
        existing.PackagePath = job.PackagePath;
        existing.CompletedUtc = job.CompletedUtc;
        existing.FailureReason = job.FailureReason;

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

    public async Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var baseQuery = dbContext.PublishJobs
            .AsNoTracking()
            .Where(x => x.ApplicationId == query.ApplicationId);

        if (!string.IsNullOrWhiteSpace(query.SequenceNumber))
        {
            baseQuery = baseQuery.Where(x => x.SequenceNumber == query.SequenceNumber);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            baseQuery = baseQuery.Where(x => x.Status == query.Status);
        }

        if (query.CreatedFromUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.CreatedUtc >= query.CreatedFromUtc.Value);
        }

        if (query.CreatedToUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.CreatedUtc <= query.CreatedToUtc.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var completedCount = await baseQuery.CountAsync(x => x.Status == PublishJobStatus.Completed.ToString(), cancellationToken);
        var failedCount = await baseQuery.CountAsync(x => x.Status == PublishJobStatus.Failed.ToString(), cancellationToken);
        var runningCount = await baseQuery.CountAsync(x => x.Status == PublishJobStatus.Running.ToString(), cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var records = await baseQuery
            .OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new PublishJobHistoryQueryResult(
            records.Select(x => x.ToDomain()).ToArray(),
            totalCount,
            completedCount,
            failedCount,
            runningCount);
    }

    public async Task DeleteByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var records = await dbContext.PublishJobs
            .Where(x => x.ApplicationId == applicationId)
            .ToArrayAsync(cancellationToken);

        if (records.Length == 0)
        {
            return;
        }

        dbContext.PublishJobs.RemoveRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var records = await dbContext.PublishJobs
            .Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber)
            .ToArrayAsync(cancellationToken);

        if (records.Length == 0)
        {
            return;
        }

        dbContext.PublishJobs.RemoveRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
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
            PackagePath = job.PackagePath,
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
            record.PackagePath,
            record.CreatedUtc,
            record.CompletedUtc,
            record.FailureReason);
    }
}
