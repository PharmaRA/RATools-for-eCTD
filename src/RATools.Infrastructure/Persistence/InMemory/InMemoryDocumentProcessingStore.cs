using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryDocumentProcessingStore : IDocumentProcessingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DocumentVersion> _versions = [];
    private readonly Dictionary<Guid, PdfProcessingPlan> _plans = [];
    private readonly Dictionary<Guid, PdfProcessingJob> _jobs = [];
    private readonly TransactionalMemoryCollection<PdfProcessingAdoption> _adoptions = new(item => item with { });

    public Task AddVersionAsync(DocumentVersion version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(version);
        lock (_gate)
        {
            if (_versions.ContainsKey(version.Id)) throw new InvalidOperationException($"Document version {version.Id} already exists.");
            _versions.Add(version.Id, version);
        }
        return Task.CompletedTask;
    }

    public Task<DocumentVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_versions.GetValueOrDefault(id));
    }

    public Task<bool> HasApplicationEvidenceAsync(Guid applicationId, string? sequenceNumber = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_versions.Values.Any(version => version.ApplicationId == applicationId &&
            (sequenceNumber == null || version.SequenceNumber == sequenceNumber)));
    }

    public Task<bool> HasDocumentEvidenceAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_versions.Values.Any(version => version.SourceDocumentId == documentId) ||
            _adoptions.Values.Any(adoption => adoption.DocumentId == documentId));
    }

    public Task AddPlanAsync(PdfProcessingPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plan);
        plan.Verify();
        lock (_gate)
        {
            if (_plans.ContainsKey(plan.Id)) throw new InvalidOperationException($"PDF processing plan {plan.Id} already exists.");
            _plans.Add(plan.Id, plan);
        }
        return Task.CompletedTask;
    }

    public Task<PdfProcessingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var plan = _plans.GetValueOrDefault(id);
            plan?.Verify();
            return Task.FromResult(plan);
        }
    }

    public Task<PdfProcessingEnqueueResult> EnqueueAsync(PdfProcessingJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(job);
        if (job.Status != PdfProcessingJobStatus.Pending) throw new ArgumentException("A new processing job must be pending.", nameof(job));
        lock (_gate)
        {
            var plan = _plans.GetValueOrDefault(job.PlanId) ?? throw new InvalidDataException("Processing plan was not found.");
            plan.Verify();
            if (job.PlanDigest != plan.PlanDigest || job.RequestDigest != PdfProcessingJob.Create(plan, job.IdempotencyKey).RequestDigest)
                throw new InvalidDataException("Processing request does not match its plan.");
            var sameKey = _jobs.Values.SingleOrDefault(item => item.IdempotencyKey == job.IdempotencyKey);
            if (sameKey is not null)
            {
                if (!string.Equals(sameKey.RequestDigest, job.RequestDigest, StringComparison.Ordinal))
                    throw new PdfProcessingIdempotencyConflictException(job.IdempotencyKey);
                return Task.FromResult(new PdfProcessingEnqueueResult(sameKey, false));
            }

            var samePlan = _jobs.Values.SingleOrDefault(item => item.PlanId == job.PlanId);
            if (samePlan is not null && !string.Equals(samePlan.RequestDigest, job.RequestDigest, StringComparison.Ordinal))
                throw new PdfProcessingIdempotencyConflictException(job.IdempotencyKey);
            _jobs.Add(job.Id, job);
            return Task.FromResult(new PdfProcessingEnqueueResult(job, true));
        }
    }

    public Task<PdfProcessingJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_jobs.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<PdfProcessingJob>> ListJobsAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<PdfProcessingJob>>(_jobs.Values.Where(job => job.PlanId == planId).ToArray());
    }

    public Task<PdfProcessingAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_adoptions.Get(jobId));
    }

    public Task AddAdoptionAsync(PdfProcessingAdoption adoption, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_adoptions.Get(adoption.JobId) is not null) throw new InvalidOperationException("Processing result was already adopted.");
        _adoptions.Set(adoption.JobId, adoption);
        return Task.CompletedTask;
    }

    public Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        lock (_gate)
        {
            foreach (var expired in _jobs.Values.Where(item => item.Status == PdfProcessingJobStatus.Running &&
                         item.LeaseExpiresUtc <= nowUtc && (item.CancellationRequested || item.AttemptCount >= 3)).ToArray())
                _jobs[expired.Id] = expired with
                {
                    Status = expired.CancellationRequested ? PdfProcessingJobStatus.Cancelled : PdfProcessingJobStatus.Failed,
                    Stage = expired.CancellationRequested ? "cancelled" : "failed", CompletedUtc = nowUtc,
                    FailureCode = expired.CancellationRequested ? "CANCELLED" : "ATTEMPTS_EXHAUSTED",
                    LeaseToken = null, LeaseOwner = null, LeaseExpiresUtc = null
                };
            var job = _jobs.Values.Where(item => item.Status == PdfProcessingJobStatus.Pending ||
                    (item.Status == PdfProcessingJobStatus.Running && item.LeaseExpiresUtc <= nowUtc))
                .OrderBy(item => item.CreatedUtc).ThenBy(item => item.Id).FirstOrDefault();
            if (job is null) return Task.FromResult<PdfProcessingLease?>(null);
            var token = Guid.NewGuid();
            var claimed = job with
            {
                Status = PdfProcessingJobStatus.Running,
                Stage = "running",
                StartedUtc = job.StartedUtc ?? nowUtc,
                AttemptCount = job.AttemptCount + 1,
                LeaseToken = token,
                LeaseOwner = owner.Trim(),
                LeaseExpiresUtc = nowUtc + leaseDuration
            };
            _jobs[job.Id] = claimed;
            return Task.FromResult<PdfProcessingLease?>(new(claimed, token, owner.Trim()));
        }
    }

    public Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration, string stage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job) || !Owns(job, token, owner, nowUtc)) return Task.FromResult(false);
            _jobs[id] = job with { Stage = string.IsNullOrWhiteSpace(stage) ? job.Stage : stage.Trim(), LeaseExpiresUtc = nowUtc + leaseDuration };
            return Task.FromResult(true);
        }
    }

    public Task<PdfProcessingJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job)) return Task.FromResult<PdfProcessingJob?>(null);
            if (job.Status is PdfProcessingJobStatus.Completed or PdfProcessingJobStatus.Failed or PdfProcessingJobStatus.Cancelled or PdfProcessingJobStatus.TimedOut)
                return Task.FromResult<PdfProcessingJob?>(job);
            var cancelled = job with
            {
                Status = job.Status == PdfProcessingJobStatus.Pending ? PdfProcessingJobStatus.Cancelled : job.Status,
                Stage = "cancellation-requested",
                CancellationRequested = true,
                CompletedUtc = job.Status == PdfProcessingJobStatus.Pending ? nowUtc : null,
                LeaseExpiresUtc = job.Status == PdfProcessingJobStatus.Pending ? nowUtc : job.LeaseExpiresUtc
            };
            _jobs[id] = cancelled;
            return Task.FromResult<PdfProcessingJob?>(cancelled);
        }
    }

    public Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, DocumentVersion result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job) || !Owns(job, token, owner, nowUtc) || job.CancellationRequested)
                return Task.FromResult(false);
            _plans[job.PlanId].VerifyResult(result);
            _versions.Add(result.Id, result);
            _jobs[id] = job with
            {
                Status = PdfProcessingJobStatus.Completed,
                Stage = "completed",
                CompletedUtc = nowUtc,
                ResultVersionId = result.Id,
                ResultPath = result.StoragePath,
                ResultSha256 = result.Sha256,
                ResultMd5 = result.Md5,
                LeaseToken = null,
                LeaseOwner = null,
                LeaseExpiresUtc = null
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status, string code, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (status is not (PdfProcessingJobStatus.Failed or PdfProcessingJobStatus.Cancelled or PdfProcessingJobStatus.TimedOut))
            throw new ArgumentException("Failure status must be terminal.", nameof(status));
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job) || !Owns(job, token, owner, nowUtc)) return Task.FromResult(false);
            _jobs[id] = job with
            {
                Status = status,
                Stage = status.ToString().ToLowerInvariant(),
                CompletedUtc = nowUtc,
                FailureCode = code,
                FailureMessage = message,
                LeaseToken = null,
                LeaseOwner = null,
                LeaseExpiresUtc = null
            };
            return Task.FromResult(true);
        }
    }

    private static bool Owns(PdfProcessingJob job, Guid token, string owner, DateTime nowUtc)
        => job.Status == PdfProcessingJobStatus.Running && job.LeaseToken == token &&
            string.Equals(job.LeaseOwner, owner, StringComparison.Ordinal) && job.LeaseExpiresUtc > nowUtc;
}
