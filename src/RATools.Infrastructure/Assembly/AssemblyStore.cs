using System.Text.Json;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;

namespace RATools.Infrastructure.Assembly;

public abstract class AssemblyStore : IAssemblyStore
{
    public abstract Task AddPlanAsync(AssemblyPreview plan, CancellationToken cancellationToken = default);
    public abstract Task<AssemblyPreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    public abstract Task<AssemblyExecutionJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    public abstract Task<AssemblyExecutionJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default);
    protected abstract Task<AssemblyEnqueueResult> InsertAsync(AssemblyExecutionJob job, CancellationToken cancellationToken);
    protected abstract Task<bool> ReplaceAsync(AssemblyExecutionJob before, AssemblyExecutionJob after, CancellationToken cancellationToken);
    protected abstract Task<IReadOnlyList<AssemblyExecutionJob>> CandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken);
    protected static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public async Task<AssemblyEnqueueResult> EnqueueAsync(PdfProcessingJob state, CancellationToken cancellationToken = default)
    {
        var plan = await VerifyRequestAsync(state, cancellationToken).ConfigureAwait(false);
        if (!plan.CanExecute || state.Status != PdfProcessingJobStatus.Pending || state.AttemptCount != 0 || state.LeaseToken is not null)
            throw new InvalidDataException("Only a reviewed fresh assembly request may enqueue.");
        return await InsertAsync(new(state, null), cancellationToken).ConfigureAwait(false);
    }
    private async Task<AssemblyPreview> VerifyRequestAsync(PdfProcessingJob state, CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(state.PlanId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("The assembly preview is missing.");
        plan.Verify();
        if (plan.Plan.PlanDigest != state.PlanDigest || AssemblyEvidence.Request(plan, state.IdempotencyKey).RequestDigest != state.RequestDigest)
            throw new InvalidDataException("The assembly request no longer matches the plan.");
        return plan;
    }
    public async Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        Lease(owner, duration);
        foreach (var candidate in await CandidatesAsync(nowUtc, cancellationToken).ConfigureAwait(false))
        {
            var state = candidate.State;
            if (state.CancellationRequested || state.AttemptCount >= 3)
            {
                await ReplaceAsync(candidate, candidate with { State = Finish(state, state.CancellationRequested ? PdfProcessingJobStatus.Cancelled : PdfProcessingJobStatus.Failed,
                    nowUtc, state.CancellationRequested ? "CANCELLED" : "ATTEMPTS_EXHAUSTED", "Assembly attempt did not complete.") }, cancellationToken).ConfigureAwait(false);
                continue;
            }
            try { await VerifyRequestAsync(state, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException)
            {
                await ReplaceAsync(candidate, candidate with { State = Finish(state, PdfProcessingJobStatus.Failed, nowUtc,
                    "REQUEST_INTEGRITY_FAILED", "Assembly evidence failed integrity verification.") }, cancellationToken).ConfigureAwait(false); continue;
            }
            var token = Guid.NewGuid();
            var next = state with { Status = PdfProcessingJobStatus.Running, Stage = "preparing-assembly", AttemptCount = state.AttemptCount + 1,
                StartedUtc = state.StartedUtc ?? nowUtc, LeaseToken = token, LeaseOwner = owner, LeaseExpiresUtc = nowUtc + duration };
            if (await ReplaceAsync(candidate, candidate with { State = next }, cancellationToken).ConfigureAwait(false)) return new(next, token, owner);
        }
        return null;
    }
    public Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        Lease(owner, duration);
        return ChangeAsync(id, before => Owns(before.State, token, owner, nowUtc) && !before.State.CancellationRequested
            ? before with { State = before.State with { LeaseExpiresUtc = nowUtc + duration } } : null, cancellationToken);
    }
    public async Task<AssemblyExecutionJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(id, before => before.State.Status switch
        {
            PdfProcessingJobStatus.Pending => before with { State = Finish(before.State with { CancellationRequested = true }, PdfProcessingJobStatus.Cancelled, nowUtc, "CANCELLED", null) },
            PdfProcessingJobStatus.Running => before with { State = before.State with { CancellationRequested = true } },
            _ => null
        }, cancellationToken).ConfigureAwait(false);
        return await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
    }
    public async Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, AssemblyExecutionAudit result, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
        if (job is null || !Owns(job.State, token, owner, nowUtc) || job.State.CancellationRequested) return false;
        var plan = await VerifyRequestAsync(job.State, cancellationToken).ConfigureAwait(false);
        if (result.JobId != id || result.PlanId != plan.Id || result.PlanDigest != plan.Plan.PlanDigest || result.ApplicationId != plan.Plan.ApplicationId ||
            result.SequenceNumber != plan.Plan.SequenceNumber || result.ExpectedRevision != plan.Plan.ExpectedRevision || result.WorkspaceRevision != result.ExpectedRevision + 1 ||
            !result.Documents.Select(item => item.LineNumber).Order().SequenceEqual(plan.SelectedRows) ||
            result.Documents.Select(item => item.PlacementId).Distinct().Count() != plan.SelectedRows.Count)
            throw new InvalidDataException("Assembly execution must commit the complete approved unit.");
        return await ChangeAsync(id, before => Owns(before.State, token, owner, nowUtc) && !before.State.CancellationRequested
            ? new(Finish(before.State, PdfProcessingJobStatus.Completed, nowUtc, null, null), result) : null, cancellationToken).ConfigureAwait(false);
    }
    public Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status, string code, string message, CancellationToken cancellationToken = default)
    {
        if (status is not (PdfProcessingJobStatus.Failed or PdfProcessingJobStatus.Cancelled or PdfProcessingJobStatus.TimedOut)) throw new ArgumentException("Invalid assembly terminal state.", nameof(status));
        return ChangeAsync(id, before => Owns(before.State, token, owner, nowUtc) ? before with { State = Finish(before.State, status, nowUtc, code, message) } : null, cancellationToken);
    }
    private async Task<bool> ChangeAsync(Guid id, Func<AssemblyExecutionJob, AssemblyExecutionJob?> change, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); var before = await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
            if (before is null || change(before) is not { } after) return false;
            if (await ReplaceAsync(before, after, cancellationToken).ConfigureAwait(false)) return true;
        }
    }
    private static bool Owns(PdfProcessingJob state, Guid token, string owner, DateTime now) => state.Status == PdfProcessingJobStatus.Running && state.LeaseToken == token && state.LeaseOwner == owner && state.LeaseExpiresUtc > now;
    private static PdfProcessingJob Finish(PdfProcessingJob state, PdfProcessingJobStatus status, DateTime now, string? code, string? message) =>
        state with { Status = status, Stage = status.ToString().ToLowerInvariant(), CompletedUtc = now, FailureCode = code, FailureMessage = message, LeaseToken = null, LeaseOwner = null, LeaseExpiresUtc = null };
    private static void Lease(string owner, TimeSpan duration)
    { ArgumentException.ThrowIfNullOrWhiteSpace(owner); if (owner.Length > 128 || owner != owner.Trim()) throw new ArgumentException("Invalid lease owner.", nameof(owner)); ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero); }
}
