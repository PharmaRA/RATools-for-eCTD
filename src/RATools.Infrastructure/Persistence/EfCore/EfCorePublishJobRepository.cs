using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCorePublishJobRepository(RAToolsDbContext dbContext) : IPublishJobRepository
{
    public async Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.PublishJobs.AddAsync(job.ToRecord(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActivePublishJobConflict(exception))
        {
            throw new PublishJobAlreadyInProgressException($"A publish job is already pending or running for application {job.ApplicationId}, sequence {job.SequenceNumber}.");
        }
    }

    public async Task<PublishJobEnqueueResult> AddOrGetByIdempotencyKeyAsync(
        PublishJob job,
        CancellationToken cancellationToken = default)
    {
        var record = job.ToRecord();
        try
        {
            await dbContext.PublishJobs.AddAsync(record, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new PublishJobEnqueueResult(job, Created: true);
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            var existing = await dbContext.PublishJobs
                .AsNoTracking()
                .SingleAsync(x => x.IdempotencyKey == job.IdempotencyKey, cancellationToken);

            if (existing.ApplicationId != job.ApplicationId
                || !string.Equals(existing.SequenceNumber, job.SequenceNumber, StringComparison.Ordinal))
            {
                throw new PublishJobIdempotencyConflictException(job.IdempotencyKey);
            }

            return new PublishJobEnqueueResult(existing.ToDomain(), Created: false);
        }
        catch (DbUpdateException exception) when (IsActivePublishJobConflict(exception))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            var existing = await dbContext.PublishJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == job.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (existing.ApplicationId != job.ApplicationId
                    || !string.Equals(existing.SequenceNumber, job.SequenceNumber, StringComparison.Ordinal))
                {
                    throw new PublishJobIdempotencyConflictException(job.IdempotencyKey);
                }

                return new PublishJobEnqueueResult(existing.ToDomain(), Created: false);
            }

            throw new PublishJobAlreadyInProgressException($"A publish job is already pending or running for application {job.ApplicationId}, sequence {job.SequenceNumber}.");
        }
    }

    private static bool IsActivePublishJobConflict(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgresException.ConstraintName, "IX_publish_jobs_ApplicationId_SequenceNumber", StringComparison.Ordinal);
        }

        return exception.InnerException?.Message.Contains(
            "publish_jobs.ApplicationId, publish_jobs.SequenceNumber",
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsIdempotencyKeyConflict(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgresException.ConstraintName, "IX_publish_jobs_IdempotencyKey", StringComparison.Ordinal);
        }

        return exception.InnerException?.Message.Contains(
            "publish_jobs.IdempotencyKey",
            StringComparison.OrdinalIgnoreCase) == true;
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
        existing.AttemptCount = job.AttemptCount;
        existing.NextAttemptUtc = job.NextAttemptUtc;
        existing.LeaseOwner = job.LeaseOwner;
        existing.LeaseToken = job.LeaseToken;
        existing.LeaseExpiresUtc = job.LeaseExpiresUtc;
        existing.LastHeartbeatUtc = job.LastHeartbeatUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.PublishJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return record?.ToDomain();
    }

    public async Task<PublishJob?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.PublishJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        return record?.ToDomain();
    }

    public async Task<PublishJobLease?> TryClaimNextAsync(
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        var pendingStatus = PublishJobStatus.Pending.ToString();
        var runningStatus = PublishJobStatus.Running.ToString();
        var leaseExpiresUtc = nowUtc.Add(leaseDuration);

        while (true)
        {
            var candidateId = await dbContext.PublishJobs
                .AsNoTracking()
                .Where(x => x.Status == pendingStatus
                    && x.NextAttemptUtc <= nowUtc
                    && x.AttemptCount < maxAttempts)
                .OrderBy(x => x.NextAttemptUtc)
                .ThenBy(x => x.CreatedUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!candidateId.HasValue)
            {
                return null;
            }

            var leaseToken = Guid.NewGuid();
            var affected = await dbContext.PublishJobs
                .Where(x => x.Id == candidateId.Value
                    && x.Status == pendingStatus
                    && x.NextAttemptUtc <= nowUtc
                    && x.AttemptCount < maxAttempts)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, runningStatus)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LeaseOwner, owner)
                    .SetProperty(x => x.LeaseToken, leaseToken)
                    .SetProperty(x => x.LeaseExpiresUtc, leaseExpiresUtc)
                    .SetProperty(x => x.LastHeartbeatUtc, nowUtc)
                    .SetProperty(x => x.FailureReason, (string?)null),
                    cancellationToken);

            if (affected == 0)
            {
                continue;
            }

            var claimed = await dbContext.PublishJobs
                .AsNoTracking()
                .SingleAsync(x => x.Id == candidateId.Value && x.LeaseToken == leaseToken, cancellationToken);
            return new PublishJobLease(claimed.ToDomain(), leaseToken, owner, leaseExpiresUtc);
        }
    }

    public async Task<bool> RenewLeaseAsync(
        Guid jobId,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        var runningStatus = PublishJobStatus.Running.ToString();
        var affected = await dbContext.PublishJobs
            .Where(x => x.Id == jobId
                && x.Status == runningStatus
                && x.LeaseToken == leaseToken
                && x.LeaseOwner == owner
                && x.LeaseExpiresUtc > nowUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LastHeartbeatUtc, nowUtc)
                .SetProperty(x => x.LeaseExpiresUtc, nowUtc.Add(leaseDuration)),
                cancellationToken);
        return affected == 1;
    }

    public async Task<bool> UpdateLeasedAsync(
        PublishJob job,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var runningStatus = PublishJobStatus.Running.ToString();
        var affected = await dbContext.PublishJobs
            .Where(x => x.Id == job.Id
                && x.Status == runningStatus
                && x.LeaseToken == leaseToken
                && x.LeaseOwner == owner
                && x.LeaseExpiresUtc > nowUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, job.Status.ToString())
                .SetProperty(x => x.OutputPath, job.OutputPath)
                .SetProperty(x => x.PackagePath, job.PackagePath)
                .SetProperty(x => x.CompletedUtc, job.CompletedUtc)
                .SetProperty(x => x.FailureReason, job.FailureReason)
                .SetProperty(x => x.NextAttemptUtc, job.NextAttemptUtc)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresUtc, (DateTime?)null)
                .SetProperty(x => x.LastHeartbeatUtc, (DateTime?)null),
                cancellationToken);
        return affected == 1;
    }

    public async Task<PublishJobRetryResult> RetryOrFailLeasedAsync(
        Guid jobId,
        Guid leaseToken,
        string owner,
        DateTime nowUtc,
        DateTime nextAttemptUtc,
        int maxAttempts,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        var runningStatus = PublishJobStatus.Running.ToString();
        var attemptCount = await dbContext.PublishJobs
            .AsNoTracking()
            .Where(x => x.Id == jobId
                && x.Status == runningStatus
                && x.LeaseToken == leaseToken
                && x.LeaseOwner == owner
                && x.LeaseExpiresUtc > nowUtc)
            .Select(x => (int?)x.AttemptCount)
            .SingleOrDefaultAsync(cancellationToken);

        if (!attemptCount.HasValue)
        {
            return new PublishJobRetryResult(PublishJobRetryDisposition.LeaseLost, null);
        }

        var failed = attemptCount.Value >= maxAttempts;
        var targetStatus = failed ? PublishJobStatus.Failed.ToString() : PublishJobStatus.Pending.ToString();
        var normalizedReason = failureReason.Length <= 1024 ? failureReason : failureReason[..1024];
        var affected = await dbContext.PublishJobs
            .Where(x => x.Id == jobId
                && x.Status == runningStatus
                && x.LeaseToken == leaseToken
                && x.LeaseOwner == owner
                && x.LeaseExpiresUtc > nowUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, targetStatus)
                .SetProperty(x => x.FailureReason, normalizedReason)
                .SetProperty(x => x.CompletedUtc, failed ? nowUtc : (DateTime?)null)
                .SetProperty(x => x.NextAttemptUtc, failed ? nowUtc : nextAttemptUtc)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresUtc, (DateTime?)null)
                .SetProperty(x => x.LastHeartbeatUtc, (DateTime?)null),
                cancellationToken);

        if (affected == 0)
        {
            return new PublishJobRetryResult(PublishJobRetryDisposition.LeaseLost, null);
        }

        var updated = await GetAsync(jobId, cancellationToken);
        return new PublishJobRetryResult(
            failed ? PublishJobRetryDisposition.Failed : PublishJobRetryDisposition.RetryScheduled,
            updated);
    }

    public async Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await dbContext.PublishJobs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return records.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var pendingStatus = PublishJobStatus.Pending.ToString();
        var runningStatus = PublishJobStatus.Running.ToString();
        var records = await dbContext.PublishJobs
            .AsNoTracking()
            .Where(x => x.Status == pendingStatus || x.Status == runningStatus)
            .OrderBy(x => x.CreatedUtc)
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

        var countsByStatus = await baseQuery
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToArrayAsync(cancellationToken);
        var totalCount = countsByStatus.Sum(x => x.Count);
        var statusCounts = countsByStatus.ToDictionary(x => x.Status, x => x.Count, StringComparer.Ordinal);
        var completedCount = statusCounts.GetValueOrDefault(PublishJobStatus.Completed.ToString(), 0);
        var failedCount = statusCounts.GetValueOrDefault(PublishJobStatus.Failed.ToString(), 0);
        var runningCount = statusCounts.GetValueOrDefault(PublishJobStatus.Running.ToString(), 0);

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
            FailureReason = job.FailureReason,
            IdempotencyKey = job.IdempotencyKey,
            AttemptCount = job.AttemptCount,
            NextAttemptUtc = job.NextAttemptUtc,
            LeaseOwner = job.LeaseOwner,
            LeaseToken = job.LeaseToken,
            LeaseExpiresUtc = job.LeaseExpiresUtc,
            LastHeartbeatUtc = job.LastHeartbeatUtc
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
            record.FailureReason,
            record.IdempotencyKey,
            record.AttemptCount,
            record.NextAttemptUtc,
            record.LeaseOwner,
            record.LeaseToken,
            record.LeaseExpiresUtc,
            record.LastHeartbeatUtc);
    }
}
