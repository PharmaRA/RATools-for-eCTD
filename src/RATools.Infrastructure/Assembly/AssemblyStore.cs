using System.Text.Json;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Assembly;

public abstract class AssemblyStore(IDocumentRepository? documents, IDocumentPlacementRepository? placements) : IAssemblyStore
{
    protected abstract bool HasCompletionTransaction { get; }
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
        VerifyResult(plan, id, result);
        if (!HasCompletionTransaction) throw new InvalidOperationException("Assembly completion requires the workspace transaction.");
        if (documents is null || placements is null)
            throw new InvalidDataException("Assembly completion requires the authoritative document and placement stores.");
        foreach (var output in result.Documents)
        {
            var row = plan.Plan.Rows.Single(item => item.LineNumber == output.LineNumber);
            var placement = await placements.GetAsync(output.PlacementId, cancellationToken).ConfigureAwait(false);
            var document = await documents.GetAsync(output.DocumentId, cancellationToken).ConfigureAwait(false);
            if (placement is null || document is null || placement.DocumentId != output.DocumentId ||
                placement.ApplicationId != plan.Plan.ApplicationId || placement.SequenceNumber != plan.Plan.SequenceNumber ||
                placement.NodeInstanceId?.ToString("D") != row.NodeInstanceId || placement.SortOrder != row.SortOrder ||
                placement.Title != row.Title || !string.Equals(placement.Operation.ToString(), row.Operation, StringComparison.OrdinalIgnoreCase) ||
                placement.LeafId != output.LeafId || placement.LifecycleTargetPlacementId != output.HistoricalTargetId)
                throw new InvalidDataException("Assembly result row does not describe the actual approved placement.");
            if (row.Operation == "delete")
            {
                var target = await placements.GetAsync(output.HistoricalTargetId!.Value, cancellationToken).ConfigureAwait(false);
                if (target is null || target.DocumentId != document.Id || target.ApplicationId != plan.Plan.ApplicationId ||
                    string.CompareOrdinal(target.SequenceNumber, plan.Plan.SequenceNumber) >= 0)
                    throw new InvalidDataException("Assembly deletion result lost its historical document identity.");
            }
            else
            {
                var source = plan.Sources.Single(item => item.LineNumber == row.LineNumber);
                if (document.FileName != source.FileName || document.MediaType != source.MediaType || document.FileSize != source.Length ||
                    document.Sha256 != source.Sha256 || document.Md5 != source.Md5 ||
                    PublishOutputNaming.BuildPublishedDocumentRelativePath(document, plan.Plan.SequenceNumber) != source.TargetHref)
                    throw new InvalidDataException("Assembly output document differs from its sealed source and destination.");
            }
        }
        return await ChangeAsync(id, before => Owns(before.State, token, owner, nowUtc) && !before.State.CancellationRequested
            ? new(Finish(before.State, PdfProcessingJobStatus.Completed, nowUtc, null, null), result) : null, cancellationToken).ConfigureAwait(false);
    }

    private static void VerifyResult(AssemblyPreview plan, Guid id, AssemblyExecutionAudit result)
    {
        if (result.JobId != id || result.PlanId != plan.Id || result.PlanDigest != plan.Plan.PlanDigest || result.ApplicationId != plan.Plan.ApplicationId ||
            result.SequenceNumber != plan.Plan.SequenceNumber || result.ExpectedRevision != plan.Plan.ExpectedRevision || result.WorkspaceRevision != result.ExpectedRevision + 1 ||
            !result.Documents.Select(item => item.LineNumber).Order().SequenceEqual(plan.SelectedRows) ||
            result.Documents.Select(item => item.PlacementId).Distinct().Count() != plan.SelectedRows.Count)
            throw new InvalidDataException("Assembly execution must commit the complete approved unit.");
        foreach (var output in result.Documents)
        {
            var row = plan.Plan.Rows.Single(item => item.LineNumber == output.LineNumber);
            var target = row.TargetLeafAddress is null ? (Guid?)null : Guid.Parse(row.TargetLeafAddress);
            if (output.PlacementId == Guid.Empty || output.DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(output.LeafId) ||
                output.HistoricalTargetId != target)
                throw new InvalidDataException("Assembly result row has an invalid output or historical identity.");
            if (row.Operation == "delete")
            {
                if (output.VersionId is not null || output.Href is not null || target is null)
                    throw new InvalidDataException("Assembly deletion result must not invent a payload version or destination.");
            }
            else
            {
                var source = plan.Sources.SingleOrDefault(item => item.LineNumber == row.LineNumber)
                    ?? throw new InvalidDataException("Assembly result is missing its sealed source binding.");
                if (output.VersionId != source.VersionId || source.VersionId == Guid.Empty || output.Href != source.TargetHref || output.DocumentId == source.DocumentId)
                    throw new InvalidDataException("Assembly result row differs from its sealed source version or destination.");
            }
        }
    }

    protected async Task VerifyStoredResultAsync(AssemblyExecutionJob job, CancellationToken cancellationToken)
    {
        if (job.State.Status == PdfProcessingJobStatus.Completed)
        {
            var plan = await VerifyRequestAsync(job.State, cancellationToken).ConfigureAwait(false);
            VerifyResult(plan, job.State.Id, job.Result ?? throw new InvalidDataException("Completed assembly evidence has no result."));
        }
        else if (job.Result is not null) throw new InvalidDataException("Non-completed assembly evidence contains a result.");
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
