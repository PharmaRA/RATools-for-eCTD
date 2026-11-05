using System.Text.Json;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.Assembly;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryAssemblyStore : AssemblyStore
{
    private readonly object _gate = new();
    private readonly TransactionalMemoryCollection<AssemblyPreview> _plans = new(Clone);
    private readonly TransactionalMemoryCollection<AssemblyExecutionJob> _jobs = new(Clone);
    public override Task AddPlanAsync(AssemblyPreview plan, CancellationToken cancellationToken = default)
    {
        plan.Verify(); lock (_gate) { if (_plans.Get(plan.Id) is not null) throw new InvalidDataException("The immutable assembly plan already exists."); _plans.Set(plan.Id, plan); }
        return Task.CompletedTask;
    }
    public override Task<AssemblyPreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_plans.Get(id));
    public override Task<AssemblyExecutionJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_jobs.Get(id));
    public override Task<AssemblyExecutionJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(_jobs.Values.SingleOrDefault(item => item.State.PlanId == planId));
    protected override Task<AssemblyEnqueueResult> InsertAsync(AssemblyExecutionJob job, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var candidates = _jobs.Values.Where(item => item.State.IdempotencyKey == job.State.IdempotencyKey || item.State.PlanId == job.State.PlanId).ToArray();
            if (candidates.Length > 0)
            {
                var existing = candidates.FirstOrDefault(item => item.State.RequestDigest == job.State.RequestDigest)
                    ?? throw new AssemblyPlanInvalidException("The idempotency key or plan already has a different execution.");
                return Task.FromResult(new AssemblyEnqueueResult(existing, false));
            }
            _jobs.Set(job.State.Id, job); return Task.FromResult(new AssemblyEnqueueResult(Clone(job), true));
        }
    }
    protected override Task<bool> ReplaceAsync(AssemblyExecutionJob before, AssemblyExecutionJob after, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var current = _jobs.Get(before.State.Id);
            if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(before)) return Task.FromResult(false);
            _jobs.Set(after.State.Id, after); return Task.FromResult(true);
        }
    }
    protected override Task<IReadOnlyList<AssemblyExecutionJob>> CandidatesAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AssemblyExecutionJob>>(_jobs.Values.Where(item => item.State.Status == PdfProcessingJobStatus.Pending ||
            (item.State.Status == PdfProcessingJobStatus.Running && item.State.LeaseExpiresUtc <= nowUtc)).OrderBy(item => item.State.CreatedUtc).ThenBy(item => item.State.Id).Take(32).ToArray());
}
