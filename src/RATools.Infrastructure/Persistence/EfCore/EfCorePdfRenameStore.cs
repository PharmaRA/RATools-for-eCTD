using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCorePdfRenameStore(RAToolsDbContext context, IDocumentProcessingStore versions) : PdfRenameStore(versions)
{
    public override async Task AddPlanAsync(PdfRenamePreview plan, CancellationToken cancellationToken = default)
    {
        plan.Verify();
        var data = JsonSerializer.Serialize(plan);
        context.Add(new PdfRenamePlanRecord { Id = plan.Id, ApplicationId = plan.Plan.ApplicationId, Data = data, Digest = Hash(data) });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    public override async Task<PdfRenamePreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfRenamePlanRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var plan = ProcessingRecordData.Read<PdfRenamePreview>(row.Data, row.Digest); plan.Verify();
        if (plan.Id != row.Id || plan.Plan.ApplicationId != row.ApplicationId) throw new InvalidDataException("PDF rename plan identity changed.");
        return plan;
    }
    public override async Task<PdfRenameJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfRenameJobRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Read(row);
    }
    public override async Task<PdfRenameJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfRenameJobRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.PlanId == planId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Read(row);
    }
    protected override async Task<PdfRenameEnqueueResult> InsertAsync(PdfRenameJob job, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(job);
        var row = new PdfRenameJobRecord { Id = job.State.Id, PlanId = job.State.PlanId, IdempotencyKey = job.State.IdempotencyKey,
            Status = job.State.Status, CreatedUtc = job.State.CreatedUtc, Data = data, Digest = Hash(data) };
        context.Add(row);
        try { await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); return new(job, true); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.Entry(row).State = EntityState.Detached;
            var existing = await context.Set<PdfRenameJobRecord>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == job.State.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is null || Read(existing).State.RequestDigest != job.State.RequestDigest)
                throw new PdfProcessingIdempotencyConflictException(job.State.IdempotencyKey);
            return new(Read(existing), false);
        }
        finally { context.Entry(row).State = EntityState.Detached; }
    }
    protected override async Task<bool> ReplaceAsync(PdfRenameJob before, PdfRenameJob after, CancellationToken cancellationToken)
    {
        var oldDigest = Hash(JsonSerializer.Serialize(before));
        var data = JsonSerializer.Serialize(after); var digest = Hash(data);
        return await context.Set<PdfRenameJobRecord>().Where(item => item.Id == before.State.Id && item.Digest == oldDigest)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Data, data).SetProperty(item => item.Digest, digest)
                .SetProperty(item => item.Status, after.State.Status).SetProperty(item => item.LeaseExpiresUtc, after.State.LeaseExpiresUtc),
                cancellationToken).ConfigureAwait(false) == 1;
    }
    protected override async Task<IReadOnlyList<PdfRenameJob>> CandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var rows = await context.Set<PdfRenameJobRecord>().AsNoTracking().Where(item => item.Status == PdfProcessingJobStatus.Pending ||
            (item.Status == PdfProcessingJobStatus.Running && item.LeaseExpiresUtc <= nowUtc))
            .OrderBy(item => item.CreatedUtc).ThenBy(item => item.Id).Take(32).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<PdfRenameJob>();
        foreach (var row in rows)
        {
            try { candidates.Add(Read(row)); }
            catch (InvalidDataException)
            {
                // Keep the original, possibly malformed bytes and checksum intact.
                // Only quarantine the indexed queue projection using its old digest
                // and data as the fence, so unrelated valid jobs can still run.
                await context.Set<PdfRenameJobRecord>().Where(item => item.Id == row.Id && item.Digest == row.Digest &&
                    item.Data == row.Data && item.Status == row.Status && item.QuarantinedUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, PdfProcessingJobStatus.Failed)
                        .SetProperty(item => item.QuarantinedUtc, nowUtc).SetProperty(item => item.LeaseExpiresUtc, (DateTime?)null), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        return candidates;
    }
    public override async Task<PdfRenameAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<PdfRenameAdoptionRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.JobId == jobId, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var adoption = ProcessingRecordData.Read<PdfRenameAdoption>(row.Data, row.Digest);
        if (adoption.JobId != row.JobId) throw new InvalidDataException("PDF rename adoption identity changed.");
        return adoption;
    }
    public override async Task AddAdoptionAsync(PdfRenameAdoption adoption, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("PDF rename adoption requires the workspace transaction.");
        var data = JsonSerializer.Serialize(adoption);
        context.Add(new PdfRenameAdoptionRecord { JobId = adoption.JobId, Data = data, Digest = Hash(data) });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    private static PdfRenameJob Read(PdfRenameJobRecord row)
    {
        if (row.QuarantinedUtc is { } quarantined)
            return new(new(row.Id, row.PlanId, "", row.IdempotencyKey, "", PdfProcessingJobStatus.Failed, "failed",
                row.CreatedUtc, CompletedUtc: quarantined, FailureCode: "REQUEST_INTEGRITY_FAILED",
                FailureMessage: "Stored batch request evidence failed verification and was retained for review."), []);
        PdfRenameJob job;
        try { job = ProcessingRecordData.Read<PdfRenameJob>(row.Data, row.Digest); }
        catch (JsonException exception) { throw new InvalidDataException("PDF rename job evidence is malformed.", exception); }
        if (job.State is null || job.Results is null) throw new InvalidDataException("PDF rename job evidence is incomplete.");
        // PostgreSQL stores timestamps at microsecond precision; JSON evidence
        // retains original ticks, so only the indexed timestamp projection rounds.
        if (job.State.Id != row.Id || job.State.PlanId != row.PlanId || job.State.IdempotencyKey != row.IdempotencyKey ||
            job.State.Status != row.Status || Truncate(job.State.CreatedUtc) != row.CreatedUtc ||
            Truncate(job.State.LeaseExpiresUtc) != row.LeaseExpiresUtc)
            throw new InvalidDataException("PDF rename job index and evidence disagree.");
        return job;
    }
    private static DateTime? Truncate(DateTime? value) => value is { } date ? new DateTime(date.Ticks - date.Ticks % 10, DateTimeKind.Utc) : null;
}
