using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.DocumentProcessing;

namespace RATools.Infrastructure.DocumentProcessing;

public abstract class PdfRenameStore(IDocumentProcessingStore versions) : IPdfRenameStore
{
    public abstract Task AddPlanAsync(PdfRenamePreview plan, CancellationToken cancellationToken = default);
    public abstract Task<PdfRenamePreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    public abstract Task<PdfRenameJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    public abstract Task<PdfRenameJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default);
    public abstract Task<PdfRenameAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default);
    public abstract Task AddAdoptionAsync(PdfRenameAdoption adoption, CancellationToken cancellationToken = default);
    protected abstract Task<PdfRenameEnqueueResult> InsertAsync(PdfRenameJob job, CancellationToken cancellationToken);
    protected abstract Task<bool> ReplaceAsync(PdfRenameJob before, PdfRenameJob after, CancellationToken cancellationToken);
    protected abstract Task<IReadOnlyList<PdfRenameJob>> CandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken);

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public static PdfProcessingJob Request(PdfRenamePreview preview, string key) => PdfRenameRequests.Create(preview, key);

    public async Task<PdfRenameEnqueueResult> EnqueueAsync(PdfProcessingJob job, CancellationToken cancellationToken = default)
    {
        await VerifyRequestAsync(job, cancellationToken).ConfigureAwait(false);
        if (job.Status != PdfProcessingJobStatus.Pending || job.AttemptCount != 0 || job.LeaseToken is not null || job.ResultVersionId is not null)
            throw new ArgumentException("Only a new pending batch request may be enqueued.", nameof(job));
        return await InsertAsync(new(job, []), cancellationToken).ConfigureAwait(false);
    }

    protected async Task<PdfRenamePreview> VerifyRequestAsync(PdfProcessingJob job, CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(job.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The PDF rename preview is missing.");
        plan.Verify();
        if (job.PlanDigest != plan.Plan.PlanDigest || job.RequestDigest != Request(plan, job.IdempotencyKey).RequestDigest)
            throw new InvalidDataException("The PDF rename request no longer matches its immutable preview.");
        return plan;
    }

    public async Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        RequireLease(owner, duration);
        foreach (var candidate in await CandidatesAsync(nowUtc, cancellationToken).ConfigureAwait(false))
        {
            var state = candidate.State;
            if (state.CancellationRequested || state.AttemptCount >= 3)
            {
                _ = await ReplaceAsync(candidate, candidate with { State = Finish(state, state.CancellationRequested ?
                    PdfProcessingJobStatus.Cancelled : PdfProcessingJobStatus.Failed, nowUtc,
                    state.CancellationRequested ? "CANCELLED" : "ATTEMPTS_EXHAUSTED", null) }, cancellationToken).ConfigureAwait(false);
                continue;
            }
            try { await VerifyRequestAsync(state, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException)
            {
                _ = await ReplaceAsync(candidate, candidate with { State = Finish(state, PdfProcessingJobStatus.Failed,
                    nowUtc, "REQUEST_INTEGRITY_FAILED", "Stored batch evidence failed verification.") }, cancellationToken).ConfigureAwait(false);
                continue;
            }
            var token = Guid.NewGuid();
            var claimed = state with { Status = PdfProcessingJobStatus.Running, Stage = "processing-batch", StartedUtc = state.StartedUtc ?? nowUtc,
                AttemptCount = state.AttemptCount + 1, LeaseToken = token, LeaseOwner = owner, LeaseExpiresUtc = nowUtc + duration };
            if (await ReplaceAsync(candidate, candidate with { State = claimed }, cancellationToken).ConfigureAwait(false))
                return new(claimed, token, owner);
        }
        return null;
    }

    public Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan duration, string stage,
        CancellationToken cancellationToken = default)
    {
        RequireLease(owner, duration);
        if (stage.Length > 64) throw new ArgumentException("Invalid processing stage.", nameof(stage));
        return ChangeAsync(id, current => Owns(current.State, token, owner, nowUtc) && !current.State.CancellationRequested
            ? current with { State = current.State with { Stage = stage, LeaseExpiresUtc = nowUtc + duration } } : null, cancellationToken);
    }

    public async Task<PdfRenameJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await ChangeAsync(id, current => current.State.Status switch
        {
            PdfProcessingJobStatus.Pending => current with { State = Finish(current.State with { CancellationRequested = true },
                PdfProcessingJobStatus.Cancelled, nowUtc, "CANCELLED", null) },
            PdfProcessingJobStatus.Running => current with { State = current.State with { CancellationRequested = true } },
            _ => null
        }, cancellationToken).ConfigureAwait(false);
        return await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc,
        IReadOnlyList<PdfRenameOutput> results, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
        if (job is null || !Owns(job.State, token, owner, nowUtc) || job.State.CancellationRequested) return false;
        var preview = await VerifyRequestAsync(job.State, cancellationToken).ConfigureAwait(false);
        if (!results.Select(item => item.PlacementId).Order().SequenceEqual(preview.Plan.RequiredSources.Order()) ||
            results.Select(item => item.VersionId).Distinct().Count() != results.Count)
            throw new InvalidDataException("A batch must register its complete, distinct approved output set.");
        foreach (var output in results)
        {
            var source = preview.Sources.Single(item => item.PlacementId == output.PlacementId);
            var version = await versions.GetVersionAsync(output.VersionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("A batch result version is missing.");
            var href = preview.Plan.Changes.SingleOrDefault(item => item.PlacementId == output.PlacementId)?.NewHref ?? source.Href;
            if (output.Href != href || version.ParentVersionId != source.VersionId || version.SourceDocumentId != source.DocumentId ||
                version.ApplicationId != preview.Plan.ApplicationId || version.SequenceNumber != preview.Plan.SequenceNumber ||
                version.PlanDigest != preview.Plan.PlanDigest || version.EngineVersion != preview.EngineVersion ||
                version.Sha256 != output.Sha256 || version.Md5 != output.Md5)
                throw new InvalidDataException("A batch output does not match its reviewed source and final path.");
        }
        return await ChangeAsync(id, current => Owns(current.State, token, owner, nowUtc) && !current.State.CancellationRequested
            ? new(Finish(current.State, PdfProcessingJobStatus.Completed, nowUtc, null, null), results.OrderBy(item => item.PlacementId).ToArray())
            : null, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status,
        string code, string message, CancellationToken cancellationToken = default)
    {
        if (status is not (PdfProcessingJobStatus.Failed or PdfProcessingJobStatus.Cancelled or PdfProcessingJobStatus.TimedOut))
            throw new ArgumentException("A failure must have a terminal status.", nameof(status));
        return ChangeAsync(id, current => Owns(current.State, token, owner, nowUtc)
            ? current with { State = Finish(current.State, status, nowUtc, code, message) } : null, cancellationToken);
    }

    private async Task<bool> ChangeAsync(Guid id, Func<PdfRenameJob, PdfRenameJob?> change, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = await GetJobAsync(id, cancellationToken).ConfigureAwait(false);
            if (before is null || change(before) is not { } after) return false;
            if (await ReplaceAsync(before, after, cancellationToken).ConfigureAwait(false)) return true;
        }
    }

    private static bool Owns(PdfProcessingJob state, Guid token, string owner, DateTime nowUtc) =>
        state.Status == PdfProcessingJobStatus.Running && state.LeaseToken == token && state.LeaseOwner == owner && state.LeaseExpiresUtc > nowUtc;
    private static PdfProcessingJob Finish(PdfProcessingJob state, PdfProcessingJobStatus status, DateTime nowUtc, string? code, string? message) =>
        state with { Status = status, Stage = status.ToString().ToLowerInvariant(), CompletedUtc = nowUtc,
            FailureCode = code, FailureMessage = message, LeaseToken = null, LeaseOwner = null, LeaseExpiresUtc = null };
    private static void RequireLease(string owner, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 128 || owner != owner.Trim()) throw new ArgumentException("Invalid lease owner.", nameof(owner));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
    }
}
