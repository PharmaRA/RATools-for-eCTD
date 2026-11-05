using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// SourcePath is a server-side, authorized path; API responses must use the public
// input DTO rather than exposing this persistence record.
public sealed record RegisteredPackageInput(Guid Id, Guid ApplicationId, string SequenceNumber,
    string SourcePath, string? RootRelativePath, PackageSourceKind SourceKind, string InputDigest,
    string ManifestJson, PackageReadLimits Limits, DateTime CreatedUtc, bool IsOwnedUpload);

public sealed record PackageRunEnqueueResult(PackageValidationRun Run, bool Created);
public sealed record PackageValidationLease(PackageValidationRun Run, Guid Token, string Owner);
public sealed record PackageValidationPage<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);
public sealed record PackageRunSummary(Guid Id, Guid ApplicationId, string SequenceNumber, ValidationRunStatus Status,
    string Stage, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, int AttemptCount,
    bool CancellationRequested, string RequestDigest, ValidationBinding Binding, string? FailureCode,
    string? FailureMessage, bool ReportAvailable, ValidationCoverageSummary? CoverageSummary);
public sealed record PackageFindingQuery(int Offset = 0, int Limit = 100, string? RuleId = null,
    ValidationSeverity? Severity = null, CheckStatus? Status = null, string? LogicalPath = null,
    string? Category = null, string? NodePath = null);

// External evidence is returned without the (potentially large) report body; the
// report is fetched separately so evidence lists stay bounded.
public sealed record ExternalComparisonEvidence(Guid RunId, string ToolName, string ToolVersion, string PackageDigest,
    string ReportSha256, IReadOnlyList<ExternalComparisonDifference> Differences, DateTime RecordedUtc, string Digest);

public sealed record RegisterExternalComparisonRequest(string ToolName, string ToolVersion,
    IReadOnlyList<ExternalComparisonDifference> Differences, string ReportText);

public interface IPackageValidationStore
{
    Task<bool> RegisterExternalComparisonAsync(ExternalComparisonReport evidence, CancellationToken cancellationToken = default);
    Task<ExternalComparisonEvidence?> GetExternalComparisonAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<string?> GetExternalComparisonReportTextAsync(Guid runId, CancellationToken cancellationToken = default);
    // Returns the newest completed run for exactly this application/sequence whose
    // package digest is still bound to a completed report, if any.
    Task<Guid?> FindCompletedRunAsync(Guid applicationId, string sequenceNumber, string inputManifestDigest,
        CancellationToken cancellationToken = default);

    Task RegisterInputAsync(RegisteredPackageInput input, CancellationToken cancellationToken = default);
    Task<RegisteredPackageInput?> GetInputAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PackageRunEnqueueResult> EnqueueAsync(PackageValidationRun run, CancellationToken cancellationToken = default);
    Task<PackageValidationRun?> GetRunAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PackageValidationRun?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<PackageValidationRun?>(null);
    Task<PackageValidationPage<PackageRunSummary>> ListRunsAsync(Guid? applicationId, int offset, int limit, CancellationToken cancellationToken = default);
    Task<PackageValidationRun?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<PackageValidationLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration, string stage, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, ValidationReport report,
        DossierProjection? projection = null, CancellationToken cancellationToken = default);
    // The archived cumulative view is optional evidence: a run completed before P3, or
    // one whose projection was incomplete, simply has no projection to return. The
    // archived document is returned as canonical JSON bound to the run's binding.
    Task<string?> GetProjectionJsonAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, ValidationRunStatus status, string code, string message, CancellationToken cancellationToken = default);
    Task<int> RecoverExpiredAsync(DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default);
    Task<string?> GetReportJsonAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PackageValidationPage<ValidationFinding>> GetFindingsAsync(Guid id, PackageFindingQuery query, CancellationToken cancellationToken = default);
}

public sealed class PackageValidationIdempotencyConflictException(string key)
    : InvalidOperationException($"Idempotency key '{key}' already identifies a different validation request.");

public static class PackageValidationPaging
{
    public static void Validate(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 500) throw new ArgumentException("Use a nonnegative offset and a page size from 1 to 500.");
    }

    public static PackageRunSummary Summarize(PackageValidationRun run) => new(run.Id, run.ApplicationId,
        run.SequenceNumber, run.Status, run.Stage, run.CreatedUtc, run.StartedUtc, run.CompletedUtc,
        run.AttemptCount, run.CancellationRequestedUtc is not null, run.RequestDigest, run.Binding,
        run.FailureCode, run.FailureMessage, run.ReportSha256 is not null, run.CoverageSummary);
}
