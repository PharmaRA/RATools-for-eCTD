using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Infrastructure.Persistence.InMemory;

// Development/test provider. Production durability is supplied by PostgreSQL.
public sealed class InMemoryPackageValidationStore : IPackageValidationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, RegisteredPackageInput> _inputs = [];
    private readonly Dictionary<Guid, PackageValidationRun> _runs = [];
    private readonly Dictionary<Guid, string> _reports = [];
    private readonly Dictionary<Guid, ValidationFinding[]> _findings = [];

    public Task RegisterInputAsync(RegisteredPackageInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        input.Limits.Validate();
        ValidationBinding.RequireDigest(input.InputDigest);
        PackageInputManifest.RequireSequenceNumber(input.SequenceNumber);
        lock (_gate) _inputs.Add(input.Id, input);
        return Task.CompletedTask;
    }

    public Task<RegisteredPackageInput?> GetInputAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_inputs.GetValueOrDefault(id));
    }

    public Task<PackageRunEnqueueResult> EnqueueAsync(PackageValidationRun run, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (run.Status != ValidationRunStatus.Pending || run.Revision != 0) throw new ArgumentException("Enqueue a new pending run.", nameof(run));
        lock (_gate)
        {
            var existing = _runs.Values.SingleOrDefault(value => value.IdempotencyKey == run.IdempotencyKey);
            if (existing is not null)
            {
                if (existing.RequestDigest != run.RequestDigest) throw new PackageValidationIdempotencyConflictException(run.IdempotencyKey);
                return Task.FromResult(new PackageRunEnqueueResult(existing.Copy(), false));
            }
            _runs.Add(run.Id, run.Copy());
            return Task.FromResult(new PackageRunEnqueueResult(run.Copy(), true));
        }
    }

    public Task<PackageValidationRun?> GetRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_runs.GetValueOrDefault(id)?.Copy());
    }

    public Task<PackageValidationPage<PackageRunSummary>> ListRunsAsync(Guid? applicationId, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PackageValidationPaging.Validate(offset, limit);
        lock (_gate)
        {
            var selected = _runs.Values.Where(run => applicationId is null || run.ApplicationId == applicationId)
                .OrderByDescending(run => run.CreatedUtc).ThenBy(run => run.Id).ToArray();
            return Task.FromResult(new PackageValidationPage<PackageRunSummary>(selected.Skip(offset).Take(limit)
                .Select(PackageValidationPaging.Summarize).ToArray(), selected.Length, offset, limit));
        }
    }

    public Task<PackageValidationRun?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var run = _runs.GetValueOrDefault(id);
            run?.RequestCancellation(nowUtc);
            return Task.FromResult(run?.Copy());
        }
    }

    public Task<PackageValidationLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var run = _runs.Values.Where(run => run.Status == ValidationRunStatus.Pending).OrderBy(run => run.CreatedUtc).ThenBy(run => run.Id).FirstOrDefault();
            if (run is null) return Task.FromResult<PackageValidationLease?>(null);
            var token = run.Claim(owner, nowUtc, leaseDuration);
            return Task.FromResult<PackageValidationLease?>(new(run.Copy(), token, owner));
        }
    }

    public Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration, string stage,
        CancellationToken cancellationToken = default) => Mutate(id, token, owner, nowUtc,
            run => run.Heartbeat(token, owner, nowUtc, leaseDuration, stage), cancellationToken);

    public Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, ValidationRunStatus status, string code, string message,
        CancellationToken cancellationToken = default) => Mutate(id, token, owner, nowUtc,
            run => run.Fail(token, owner, nowUtc, status, code, message), cancellationToken);

    public Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, ValidationReport report,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var run = _runs.GetValueOrDefault(id);
            if (run is null || !run.OwnsLease(token, owner, nowUtc) || run.CancellationRequestedUtc is not null) return Task.FromResult(false);
            var json = PackageValidationJson.Write(report);
            run.Complete(token, owner, nowUtc, report, PackageValidationJson.Hash(json));
            _reports.Add(id, json);
            _findings.Add(id, report.Coverage.SelectMany(rule => rule.Findings).ToArray());
            return Task.FromResult(true);
        }
    }

    public Task<int> RecoverExpiredAsync(DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_runs.Values.Count(run => run.RecoverExpiredLease(nowUtc, maxAttempts)));
    }

    public Task<string?> GetReportJsonAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_reports.TryGetValue(id, out var json)) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(PackageValidationJson.VerifyReport(json, _runs[id].ReportSha256!, _runs[id].Binding));
        }
    }

    public Task<PackageValidationPage<ValidationFinding>> GetFindingsAsync(Guid id, PackageFindingQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PackageValidationPaging.Validate(query.Offset, query.Limit);
        if (query.Severity is { } severity && !Enum.IsDefined(severity) || query.Status is { } status && !Enum.IsDefined(status))
            throw new ArgumentException("Unknown finding filter.", nameof(query));
        lock (_gate)
        {
            var ruleIds = PackageFindingFilters.RuleIds(_runs.GetValueOrDefault(id), query.Category);
            var items = _findings.GetValueOrDefault(id, []).Where(finding =>
                (query.RuleId is null || finding.RuleId == query.RuleId) && (query.Severity is null || finding.Severity == query.Severity) &&
                (query.Status is null || finding.CheckStatus == query.Status) && (query.LogicalPath is null || finding.Location.LogicalPath == query.LogicalPath) &&
                (ruleIds is null || ruleIds.Contains(finding.RuleId, StringComparer.Ordinal)) &&
                (query.NodePath is null || finding.Location.NodePath?.Contains(query.NodePath, StringComparison.Ordinal) == true)).ToArray();
            return Task.FromResult(new PackageValidationPage<ValidationFinding>(items.Skip(query.Offset).Take(query.Limit).ToArray(), items.Length, query.Offset, query.Limit));
        }
    }

    private Task<bool> Mutate(Guid id, Guid token, string owner, DateTime nowUtc, Action<PackageValidationRun> mutation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var run = _runs.GetValueOrDefault(id);
            if (run is null || !run.OwnsLease(token, owner, nowUtc)) return Task.FromResult(false);
            mutation(run);
            return Task.FromResult(true);
        }
    }
}
