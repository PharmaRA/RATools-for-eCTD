using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.Assembly;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreAssemblyStore(RAToolsDbContext context) : AssemblyStore
{
    public override async Task AddPlanAsync(AssemblyPreview plan, CancellationToken cancellationToken = default)
    {
        plan.Verify(); var data = JsonSerializer.Serialize(plan);
        var row = new AssemblyPlanRecord { Id = plan.Id, ApplicationId = plan.Plan.ApplicationId, Data = data, Digest = AssemblyEvidence.Hash(data) };
        context.Add(row); await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); context.Entry(row).State = EntityState.Detached;
    }
    public override async Task<AssemblyPreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<AssemblyPlanRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        var plan = Read<AssemblyPreview>(row.Data, row.Digest); plan.Verify();
        if (plan.Id != row.Id || plan.Plan.ApplicationId != row.ApplicationId) throw new InvalidDataException("Assembly plan identity changed.");
        return plan;
    }
    public override async Task<AssemblyExecutionJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<AssemblyExecutionRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Job(row);
    }
    public override async Task<AssemblyExecutionJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<AssemblyExecutionRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.PlanId == planId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Job(row);
    }
    protected override async Task<AssemblyEnqueueResult> InsertAsync(AssemblyExecutionJob job, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(job);
        var row = new AssemblyExecutionRecord { Id = job.State.Id, PlanId = job.State.PlanId, IdempotencyKey = job.State.IdempotencyKey,
            Status = job.State.Status, CreatedUtc = job.State.CreatedUtc, Data = data, Digest = AssemblyEvidence.Hash(data) };
        context.Add(row);
        try { await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); return new(job, true); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.Entry(row).State = EntityState.Detached;
            var candidates = await context.Set<AssemblyExecutionRecord>().AsNoTracking().Where(item => item.IdempotencyKey == row.IdempotencyKey || item.PlanId == row.PlanId).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            var existing = candidates.Select(Job).FirstOrDefault(item => item.State.RequestDigest == job.State.RequestDigest)
                ?? throw new AssemblyPlanInvalidException("The assembly idempotency key or plan is already bound to another request.");
            return new(existing, false);
        }
        finally { context.Entry(row).State = EntityState.Detached; }
    }
    protected override async Task<bool> ReplaceAsync(AssemblyExecutionJob before, AssemblyExecutionJob after, CancellationToken cancellationToken)
    {
        if (after.State.Status == PdfProcessingJobStatus.Completed && context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Assembly completion requires the workspace transaction.");
        var oldDigest = AssemblyEvidence.Hash(JsonSerializer.Serialize(before)); var data = JsonSerializer.Serialize(after);
        return await context.Set<AssemblyExecutionRecord>().Where(row => row.Id == before.State.Id && row.Digest == oldDigest)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Data, data).SetProperty(row => row.Digest, AssemblyEvidence.Hash(data))
                .SetProperty(row => row.Status, after.State.Status).SetProperty(row => row.LeaseExpiresUtc, after.State.LeaseExpiresUtc), cancellationToken).ConfigureAwait(false) == 1;
    }
    protected override async Task<IReadOnlyList<AssemblyExecutionJob>> CandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var rows = await context.Set<AssemblyExecutionRecord>().AsNoTracking().Where(row => row.Status == PdfProcessingJobStatus.Pending ||
            (row.Status == PdfProcessingJobStatus.Running && row.LeaseExpiresUtc <= nowUtc)).OrderBy(row => row.CreatedUtc).ThenBy(row => row.Id).Take(32).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var valid = new List<AssemblyExecutionJob>();
        foreach (var row in rows)
        {
            try { valid.Add(Job(row)); }
            catch (InvalidDataException)
            {
                await context.Set<AssemblyExecutionRecord>().Where(item => item.Id == row.Id && item.Data == row.Data && item.Digest == row.Digest && item.Status == row.Status && item.QuarantinedUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, PdfProcessingJobStatus.Failed).SetProperty(item => item.LeaseExpiresUtc, (DateTime?)null)
                        .SetProperty(item => item.QuarantinedUtc, nowUtc), cancellationToken).ConfigureAwait(false);
            }
        }
        return valid;
    }
    private static T Read<T>(string data, string digest)
    {
        if (AssemblyEvidence.Hash(data) != digest) throw new InvalidDataException("Assembly stored evidence hash changed.");
        try { return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("Assembly evidence is null."); }
        catch (JsonException error) { throw new InvalidDataException("Assembly evidence is malformed.", error); }
    }
    private static AssemblyExecutionJob Job(AssemblyExecutionRecord row)
    {
        if (row.QuarantinedUtc is { } now) return new(new(row.Id, row.PlanId, "", row.IdempotencyKey, "", PdfProcessingJobStatus.Failed,
            "failed", row.CreatedUtc, CompletedUtc: now, FailureCode: "REQUEST_INTEGRITY_FAILED", FailureMessage: "Assembly evidence is retained after quarantine."), null);
        var job = Read<AssemblyExecutionJob>(row.Data, row.Digest);
        if (job.State is null || job.State.Id != row.Id || job.State.PlanId != row.PlanId || job.State.IdempotencyKey != row.IdempotencyKey ||
            job.State.Status != row.Status || Truncate(job.State.CreatedUtc) != row.CreatedUtc || Truncate(job.State.LeaseExpiresUtc) != row.LeaseExpiresUtc)
            throw new InvalidDataException("Assembly job projection and evidence disagree.");
        return job;
    }
    private static DateTime? Truncate(DateTime? value) => value is { } date ? new DateTime(date.Ticks - date.Ticks % 10, DateTimeKind.Utc) : null;
}
