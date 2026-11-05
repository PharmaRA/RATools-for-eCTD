using System.Text.Json;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryPdfRenameStore(IDocumentProcessingStore versions) : PdfRenameStore(versions)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PdfRenamePreview> _plans = [];
    private readonly Dictionary<Guid, PdfRenameJob> _jobs = [];
    private readonly TransactionalMemoryCollection<PdfRenameAdoption> _adoptions = new(Clone);

    public override Task AddPlanAsync(PdfRenamePreview plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); plan.Verify();
        lock (_gate) _plans.Add(plan.Id, Clone(plan));
        return Task.CompletedTask;
    }
    public override Task<PdfRenamePreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(Clone(_plans.GetValueOrDefault(id)));
    }
    public override Task<PdfRenameJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(Clone(_jobs.GetValueOrDefault(id)));
    }
    public override Task<PdfRenameJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(Clone(_jobs.Values.SingleOrDefault(item => item.State.PlanId == planId)));
    }
    protected override Task<PdfRenameEnqueueResult> InsertAsync(PdfRenameJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var existing = _jobs.Values.SingleOrDefault(item => item.State.IdempotencyKey == job.State.IdempotencyKey || item.State.PlanId == job.State.PlanId);
            if (existing is not null)
            {
                if (existing.State.RequestDigest != job.State.RequestDigest) throw new PdfProcessingIdempotencyConflictException(job.State.IdempotencyKey);
                return Task.FromResult(new PdfRenameEnqueueResult(Clone(existing), false));
            }
            _jobs.Add(job.State.Id, Clone(job));
            return Task.FromResult(new PdfRenameEnqueueResult(Clone(job), true));
        }
    }
    protected override Task<bool> ReplaceAsync(PdfRenameJob before, PdfRenameJob after, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_jobs.TryGetValue(before.State.Id, out var actual) || JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(before)) return Task.FromResult(false);
            _jobs[before.State.Id] = Clone(after); return Task.FromResult(true);
        }
    }
    protected override Task<IReadOnlyList<PdfRenameJob>> CandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<PdfRenameJob>>(_jobs.Values.Where(item => item.State.Status == PdfProcessingJobStatus.Pending ||
            (item.State.Status == PdfProcessingJobStatus.Running && item.State.LeaseExpiresUtc <= nowUtc))
            .OrderBy(item => item.State.CreatedUtc).ThenBy(item => item.State.Id).Take(32).Select(Clone).ToArray());
    }
    public override Task<PdfRenameAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(_adoptions.Get(jobId));
    }
    public override Task AddAdoptionAsync(PdfRenameAdoption adoption, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_adoptions.Get(adoption.JobId) is not null) throw new InvalidOperationException("The batch is already adopted.");
        _adoptions.Set(adoption.JobId, adoption); return Task.CompletedTask;
    }
}
