using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreDocumentProcessingStore(RAToolsDbContext context) : IDocumentProcessingStore
{
    public async Task AddVersionAsync(DocumentVersion version, CancellationToken cancellationToken = default)
    {
        context.Add(ProcessingRecordData.Record(version));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<DocumentVersionRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ProcessingRecordData.Version(row);
    }

    public async Task<bool> HasApplicationEvidenceAsync(Guid applicationId, string? sequenceNumber = null, CancellationToken cancellationToken = default)
    {
        var versions = await context.Set<DocumentVersionRecord>().AsNoTracking().Where(row => row.ApplicationId == applicationId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return versions.Any(row => sequenceNumber is null || ProcessingRecordData.Version(row).SequenceNumber == sequenceNumber);
    }

    public async Task<bool> HasDocumentEvidenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await context.Set<DocumentVersionRecord>().AnyAsync(row => row.SourceDocumentId == documentId, cancellationToken).ConfigureAwait(false) ||
        await context.Set<PdfProcessingAdoptionRecord>().AnyAsync(row => row.DocumentId == documentId, cancellationToken).ConfigureAwait(false);

    public async Task AddPlanAsync(PdfProcessingPlan plan, CancellationToken cancellationToken = default)
    {
        plan.Verify();
        var source = await GetVersionAsync(plan.SourceVersionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Processing source version was not found.");
        VerifySource(plan, source);
        var data = JsonSerializer.Serialize(plan);
        context.Add(new PdfProcessingPlanRecord { Id = plan.Id, ApplicationId = plan.ApplicationId,
            SourceVersionId = plan.SourceVersionId, Data = data, Digest = ProcessingRecordData.Hash(data) });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfProcessingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfProcessingPlanRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var plan = ProcessingRecordData.Read<PdfProcessingPlan>(row.Data, row.Digest);
        if (plan.Id != row.Id || plan.ApplicationId != row.ApplicationId || plan.SourceVersionId != row.SourceVersionId)
            throw new InvalidDataException("Stored PDF plan identity changed.");
        plan.Verify();
        return plan;
    }

    public async Task<PdfProcessingEnqueueResult> EnqueueAsync(PdfProcessingJob job, CancellationToken cancellationToken = default)
    {
        var plan = await GetPlanAsync(job.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Processing plan was not found.");
        VerifyRequest(job, plan);
        if (job.Status != PdfProcessingJobStatus.Pending || job.AttemptCount != 0 || job.LeaseToken is not null || job.ResultVersionId is not null)
            throw new ArgumentException("Only a new pending request can be enqueued.", nameof(job));
        var row = new PdfProcessingJobRecord { Id = job.Id, PlanId = job.PlanId, PlanDigest = job.PlanDigest,
            IdempotencyKey = job.IdempotencyKey, RequestDigest = job.RequestDigest, Status = job.Status,
            Stage = "queued", CreatedUtc = job.CreatedUtc };
        context.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(job, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.Entry(row).State = EntityState.Detached;
            var existing = await context.Set<PdfProcessingJobRecord>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == job.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is null || existing.RequestDigest != job.RequestDigest || existing.PlanId != job.PlanId)
                throw new PdfProcessingIdempotencyConflictException(job.IdempotencyKey);
            var result = ProcessingRecordData.Job(existing);
            VerifyRequest(result, plan);
            return new(result, false);
        }
    }

    public async Task<PdfProcessingJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfProcessingJobRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var result = ProcessingRecordData.Job(row);
        VerifyRequest(result, await GetPlanAsync(result.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Processing job plan is missing."));
        return result;
    }

    public async Task<IReadOnlyList<PdfProcessingJob>> ListJobsAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var rows = await context.Set<PdfProcessingJobRecord>().AsNoTracking().Where(item => item.PlanId == planId)
            .OrderByDescending(item => item.CreatedUtc).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ProcessingRecordData.Job).ToArray();
    }

    public async Task<PdfProcessingAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfProcessingAdoptionRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.JobId == jobId, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var value = ProcessingRecordData.Read<PdfProcessingAdoption>(row.Data, row.Digest);
        if (value.JobId != row.JobId || value.DocumentId != row.DocumentId)
            throw new InvalidDataException("Stored PDF adoption identity changed.");
        return value;
    }

    public async Task AddAdoptionAsync(PdfProcessingAdoption adoption, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("PDF adoption must be part of the workspace transaction.");
        var data = JsonSerializer.Serialize(adoption);
        context.Add(new PdfProcessingAdoptionRecord { JobId = adoption.JobId, DocumentId = adoption.DocumentId,
            Data = data, Digest = ProcessingRecordData.Hash(data) });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        RequireLease(owner, leaseDuration);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var rows = await context.Set<PdfProcessingJobRecord>().FromSqlInterpolated($"""
                    SELECT * FROM document_processing_jobs
                    WHERE "Status" = 'Pending' OR ("Status" = 'Running' AND "LeaseExpiresUtc" <= {nowUtc})
                    ORDER BY "CreatedUtc", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                    """).AsTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var row = rows.FirstOrDefault();
                if (row is null)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return null;
                }
                if (row.CancellationRequested || row.AttemptCount >= 3)
                {
                    Finish(row, row.CancellationRequested ? PdfProcessingJobStatus.Cancelled : PdfProcessingJobStatus.Failed,
                        nowUtc, row.CancellationRequested ? "CANCELLED" : "ATTEMPTS_EXHAUSTED", null);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                try
                {
                    var plan = await GetPlanAsync(row.PlanId, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Processing job plan is missing.");
                    VerifyRequest(ProcessingRecordData.Job(row), plan);
                }
                catch (InvalidDataException)
                {
                    Finish(row, PdfProcessingJobStatus.Failed, nowUtc, "REQUEST_INTEGRITY_FAILED",
                        "Stored processing request evidence failed verification; no output was produced.");
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                row.Status = PdfProcessingJobStatus.Running; row.Stage = "running";
                row.StartedUtc ??= nowUtc; row.AttemptCount++; row.LeaseToken = Guid.NewGuid();
                row.LeaseOwner = owner; row.LeaseExpiresUtc = nowUtc + leaseDuration;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(ProcessingRecordData.Job(row), row.LeaseToken.Value, owner);
            }
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration, string stage,
        CancellationToken cancellationToken = default)
    {
        RequireLease(owner, leaseDuration);
        if (stage.Length > 64) throw new ArgumentException("Processing stage is too long.", nameof(stage));
        return ChangeAsync(id, async row =>
        {
            if (!Owns(row, token, owner, nowUtc)) return false;
            row.Stage = stage; row.LeaseExpiresUtc = nowUtc + leaseDuration;
            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public async Task<PdfProcessingJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(id, row =>
        {
            if (row.Status is PdfProcessingJobStatus.Pending or PdfProcessingJobStatus.Running)
            {
                row.CancellationRequested = true;
                if (row.Status == PdfProcessingJobStatus.Pending)
                    Finish(row, PdfProcessingJobStatus.Cancelled, nowUtc, "CANCELLED", null);
            }
            return Task.FromResult(true);
        }, cancellationToken).ConfigureAwait(false);
        return await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, DocumentVersion result,
        CancellationToken cancellationToken = default) => ChangeAsync(id, async row =>
    {
        if (!Owns(row, token, owner, nowUtc) || row.CancellationRequested) return false;
        var plan = await GetPlanAsync(row.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Processing plan is missing.");
        plan.VerifyResult(result);
        context.Add(ProcessingRecordData.Record(result));
        Finish(row, PdfProcessingJobStatus.Completed, nowUtc, null, null);
        row.ResultVersionId = result.Id; row.ResultPath = result.StoragePath;
        row.ResultSha256 = result.Sha256; row.ResultMd5 = result.Md5;
        return true;
    }, cancellationToken);

    public Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status, string code,
        string message, CancellationToken cancellationToken = default)
    {
        if (status is not (PdfProcessingJobStatus.Failed or PdfProcessingJobStatus.Cancelled or PdfProcessingJobStatus.TimedOut))
            throw new ArgumentException("Failure status must be terminal.", nameof(status));
        return ChangeAsync(id, row =>
        {
            if (!Owns(row, token, owner, nowUtc)) return Task.FromResult(false);
            Finish(row, status, nowUtc, code, message);
            return Task.FromResult(true);
        }, cancellationToken);
    }

    private async Task<bool> ChangeAsync(Guid id, Func<PdfProcessingJobRecord, Task<bool>> change, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var row = await context.Set<PdfProcessingJobRecord>().FromSqlInterpolated(
                $"SELECT * FROM document_processing_jobs WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (row is null || !await change(row).ConfigureAwait(false)) return false;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { context.ChangeTracker.Clear(); }
    }

    private static bool Owns(PdfProcessingJobRecord row, Guid token, string owner, DateTime now) =>
        row.Status == PdfProcessingJobStatus.Running && row.LeaseToken == token && row.LeaseOwner == owner && row.LeaseExpiresUtc > now;

    private static void Finish(PdfProcessingJobRecord row, PdfProcessingJobStatus status, DateTime now, string? code, string? message)
    {
        row.Status = status; row.Stage = status.ToString().ToLowerInvariant(); row.CompletedUtc = now;
        row.FailureCode = code is { Length: > 128 } ? code[..128] : code;
        row.FailureMessage = message is { Length: > 2048 } ? message[..2048] : message;
        row.LeaseToken = null; row.LeaseOwner = null; row.LeaseExpiresUtc = null;
    }

    private static void RequireLease(string owner, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 128 || owner != owner.Trim()) throw new ArgumentException("Invalid lease owner.", nameof(owner));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
    }

    private static void VerifyRequest(PdfProcessingJob job, PdfProcessingPlan plan)
    {
        if (job.PlanId != plan.Id || job.PlanDigest != plan.PlanDigest ||
            job.RequestDigest != PdfProcessingJob.Create(plan, job.IdempotencyKey).RequestDigest)
            throw new InvalidDataException("Processing request does not match its plan.");
    }

    private static void VerifySource(PdfProcessingPlan plan, DocumentVersion source)
    {
        if (source.Id != plan.SourceVersionId || source.ApplicationId != plan.ApplicationId || source.SequenceNumber != plan.SequenceNumber ||
            source.SourceDocumentId != plan.SourceDocumentId || source.Sha256 != plan.SourceSha256 || source.Md5 != plan.SourceMd5)
            throw new InvalidDataException("Processing plan source binding changed.");
    }
}
