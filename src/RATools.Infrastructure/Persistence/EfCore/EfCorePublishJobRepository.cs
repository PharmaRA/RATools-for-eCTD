using System.Text.Json;
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

    public async Task<bool> UpdateHistorySummaryAsync(
        Guid jobId,
        int expectedAttemptCount,
        PublishJobHistorySummary summary,
        CancellationToken cancellationToken = default)
    {
        var completedStatus = PublishJobStatus.Completed.ToString();
        var failedStatus = PublishJobStatus.Failed.ToString();
        var missingMetadataJson = JsonSerializer.Serialize(summary.ReadinessMissingMetadataFields);

        var affected = await dbContext.PublishJobs
            .Where(x => x.Id == jobId
                && x.AttemptCount == expectedAttemptCount
                && (x.Status == completedStatus || x.Status == failedStatus))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.HistoryReportAvailable, summary.ReportAvailable)
                .SetProperty(x => x.HistoryReportReadable, summary.ReportReadable)
                .SetProperty(x => x.HistoryReportError, summary.ReportError)
                .SetProperty(x => x.HistoryValidationProfile, summary.ValidationProfile)
                .SetProperty(x => x.HistoryValidationErrorCount, summary.ErrorCount)
                .SetProperty(x => x.HistoryValidationWarningCount, summary.WarningCount)
                .SetProperty(x => x.HistoryValidationWarningSummary, summary.WarningSummary)
                .SetProperty(x => x.HistoryReadinessIsReady, summary.ReadinessIsReady)
                .SetProperty(x => x.HistoryReadinessStatus, summary.ReadinessStatus)
                .SetProperty(x => x.HistoryReadinessBlockingErrorCount, summary.ReadinessBlockingErrorCount)
                .SetProperty(x => x.HistoryReadinessWarningCount, summary.ReadinessWarningCount)
                .SetProperty(x => x.HistoryReadinessMissingMetadataFieldsJson, missingMetadataJson)
                .SetProperty(x => x.HistoryLifecycleMatchedCount, summary.LifecycleMatchedCount)
                .SetProperty(x => x.HistoryLifecycleReplaceTargetNotFoundCount, summary.LifecycleReplaceTargetNotFoundCount)
                .SetProperty(x => x.HistoryLifecycleDeleteTargetNotFoundCount, summary.LifecycleDeleteTargetNotFoundCount)
                .SetProperty(x => x.HistoryLifecycleAppendTargetNotFoundCount, summary.LifecycleAppendTargetNotFoundCount)
                .SetProperty(x => x.HistoryLifecycleAmbiguousCount, summary.LifecycleAmbiguousCount)
                .SetProperty(x => x.HistoryLifecycleCurrentSequenceCount, summary.LifecycleCurrentSequenceCount)
                .SetProperty(x => x.HistoryArtifactFileCount, summary.ArtifactFileCount)
                .SetProperty(x => x.HistoryArtifactTotalSizeBytes, summary.ArtifactTotalSizeBytes)
                .SetProperty(x => x.HistoryArtifactPackageSizeBytes, summary.ArtifactPackageSizeBytes)
                .SetProperty(x => x.HistoryReportPath, summary.ReportPath),
                cancellationToken);

        return affected == 1;
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

    public async Task<IReadOnlyCollection<PublishJob>> RecoverExpiredLeasesAsync(
        DateTime nowUtc,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        var runningStatus = PublishJobStatus.Running.ToString();
        var failedStatus = PublishJobStatus.Failed.ToString();
        var normalizedReason = failureReason.Length <= 1024 ? failureReason : failureReason[..1024];
        var recovered = new List<PublishJob>();

        while (true)
        {
            var candidateId = await dbContext.PublishJobs
                .AsNoTracking()
                .Where(x => x.Status == runningStatus
                    && (!x.LeaseExpiresUtc.HasValue || x.LeaseExpiresUtc <= nowUtc))
                .OrderBy(x => x.LeaseExpiresUtc)
                .ThenBy(x => x.CreatedUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (!candidateId.HasValue)
            {
                return recovered;
            }

            var affected = await dbContext.PublishJobs
                .Where(x => x.Id == candidateId.Value
                    && x.Status == runningStatus
                    && (!x.LeaseExpiresUtc.HasValue || x.LeaseExpiresUtc <= nowUtc))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, failedStatus)
                    .SetProperty(x => x.PackagePath, (string?)null)
                    .SetProperty(x => x.CompletedUtc, nowUtc)
                    .SetProperty(x => x.FailureReason, normalizedReason)
                    .SetProperty(x => x.LeaseOwner, (string?)null)
                    .SetProperty(x => x.LeaseToken, (Guid?)null)
                    .SetProperty(x => x.LeaseExpiresUtc, (DateTime?)null)
                    .SetProperty(x => x.LastHeartbeatUtc, (DateTime?)null),
                    cancellationToken);
            if (affected == 0)
            {
                continue;
            }

            var job = await GetAsync(candidateId.Value, cancellationToken);
            if (job is not null)
            {
                recovered.Add(job);
            }
        }
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

    private static string NormalizeReadinessStatus(string readinessStatus)
    {
        var normalized = readinessStatus.Trim();
        if (normalized.Equals("Ready", StringComparison.OrdinalIgnoreCase))
        {
            return "Ready";
        }

        if (normalized.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "Blocked";
        }

        return normalized.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "Unknown"
            : normalized;
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
            var status = Enum.TryParse<PublishJobStatus>(query.Status, ignoreCase: true, out var parsedStatus)
                ? parsedStatus.ToString()
                : query.Status.Trim();
            baseQuery = baseQuery.Where(x => x.Status == status);
        }

        if (query.CreatedFromUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.CreatedUtc >= query.CreatedFromUtc.Value);
        }

        if (query.CreatedToUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.CreatedUtc <= query.CreatedToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ReadinessStatus))
        {
            var readinessStatus = NormalizeReadinessStatus(query.ReadinessStatus);
            baseQuery = readinessStatus == "Unknown"
                ? baseQuery.Where(x => x.HistoryReadinessStatus == null)
                : baseQuery.Where(x => x.HistoryReadinessStatus == readinessStatus);
        }

        var completedStatus = PublishJobStatus.Completed.ToString();
        var failedStatus = PublishJobStatus.Failed.ToString();
        var runningStatus = PublishJobStatus.Running.ToString();
        var aggregate = await baseQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalCount = group.Count(),
                CompletedCount = group.Count(x => x.Status == completedStatus),
                FailedCount = group.Count(x => x.Status == failedStatus),
                RunningCount = group.Count(x => x.Status == runningStatus),
                ReadyCount = group.Count(x => x.HistoryReadinessStatus == "Ready"),
                BlockedCount = group.Count(x => x.HistoryReadinessStatus == "Blocked"),
                UnknownCount = group.Count(x => x.HistoryReadinessStatus == null),
                LifecycleMatchedCount = group.Sum(x => x.HistoryLifecycleMatchedCount ?? 0),
                LifecycleReplaceTargetNotFoundCount = group.Sum(x => x.HistoryLifecycleReplaceTargetNotFoundCount ?? 0),
                LifecycleDeleteTargetNotFoundCount = group.Sum(x => x.HistoryLifecycleDeleteTargetNotFoundCount ?? 0),
                LifecycleAppendTargetNotFoundCount = group.Sum(x => x.HistoryLifecycleAppendTargetNotFoundCount ?? 0),
                LifecycleAmbiguousCount = group.Sum(x => x.HistoryLifecycleAmbiguousCount ?? 0),
                LifecycleCurrentSequenceCount = group.Sum(x => x.HistoryLifecycleCurrentSequenceCount ?? 0)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var records = await baseQuery
            .OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var historySummaries = records
            .Select(record => (record.Id, Summary: record.ToHistorySummary()))
            .Where(item => item.Summary is not null)
            .ToDictionary(item => item.Id, item => item.Summary!);

        return new PublishJobHistoryQueryResult(
            records.Select(x => x.ToDomain()).ToArray(),
            aggregate?.TotalCount ?? 0,
            aggregate?.CompletedCount ?? 0,
            aggregate?.FailedCount ?? 0,
            aggregate?.RunningCount ?? 0,
            historySummaries,
            new PublishJobHistoryReadinessCounts(
                aggregate?.ReadyCount ?? 0,
                aggregate?.BlockedCount ?? 0,
                aggregate?.UnknownCount ?? 0),
            new PublishJobHistoryLifecycleCounts(
                aggregate?.LifecycleMatchedCount ?? 0,
                aggregate?.LifecycleReplaceTargetNotFoundCount ?? 0,
                aggregate?.LifecycleDeleteTargetNotFoundCount ?? 0,
                aggregate?.LifecycleAppendTargetNotFoundCount ?? 0,
                aggregate?.LifecycleAmbiguousCount ?? 0,
                aggregate?.LifecycleCurrentSequenceCount ?? 0));
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public static PublishJobHistorySummary? ToHistorySummary(this PublishJobRecord record)
    {
        if (!record.HistoryReportAvailable.HasValue)
        {
            return null;
        }

        return new PublishJobHistorySummary(
            record.HistoryReportAvailable.Value,
            record.HistoryReportReadable ?? false,
            record.HistoryReportError,
            record.HistoryValidationProfile,
            record.HistoryValidationErrorCount,
            record.HistoryValidationWarningCount,
            record.HistoryValidationWarningSummary,
            record.HistoryReadinessIsReady,
            record.HistoryReadinessStatus,
            record.HistoryReadinessBlockingErrorCount,
            record.HistoryReadinessWarningCount,
            DeserializeMissingMetadataFields(record.HistoryReadinessMissingMetadataFieldsJson),
            record.HistoryLifecycleMatchedCount ?? 0,
            record.HistoryLifecycleReplaceTargetNotFoundCount ?? 0,
            record.HistoryLifecycleDeleteTargetNotFoundCount ?? 0,
            record.HistoryLifecycleAppendTargetNotFoundCount ?? 0,
            record.HistoryLifecycleAmbiguousCount ?? 0,
            record.HistoryLifecycleCurrentSequenceCount ?? 0,
            record.HistoryArtifactFileCount,
            record.HistoryArtifactTotalSizeBytes,
            record.HistoryArtifactPackageSizeBytes,
            record.HistoryReportPath);
    }

    private static string[] DeserializeMissingMetadataFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

}
