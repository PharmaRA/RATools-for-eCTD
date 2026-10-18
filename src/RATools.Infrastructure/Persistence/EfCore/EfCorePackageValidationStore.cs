using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCorePackageValidationStore(RAToolsDbContext dbContext) : IPackageValidationStore
{
    private DbSet<PackageValidationRun> Runs => dbContext.Set<PackageValidationRun>();

    public async Task RegisterInputAsync(RegisteredPackageInput input, CancellationToken cancellationToken = default)
    {
        input.Limits.Validate();
        ValidationBinding.RequireDigest(input.InputDigest);
        PackageInputManifest.RequireSequenceNumber(input.SequenceNumber);
        var row = new PackageValidationInputRecord
        {
            Id = input.Id, ApplicationId = input.ApplicationId, SequenceNumber = input.SequenceNumber,
            SourcePath = input.SourcePath, RootRelativePath = input.RootRelativePath, SourceKind = input.SourceKind.ToString(),
            InputDigest = input.InputDigest, ManifestJson = input.ManifestJson, LimitsJson = PackageValidationJson.Write(input.Limits),
            CreatedUtc = input.CreatedUtc, IsOwnedUpload = input.IsOwnedUpload
        };
        dbContext.Add(row);
        try { await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        finally { dbContext.Entry(row).State = EntityState.Detached; }
    }

    public async Task<RegisteredPackageInput?> GetInputAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Set<PackageValidationInputRecord>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : new(row.Id, row.ApplicationId, row.SequenceNumber, row.SourcePath, row.RootRelativePath,
            Enum.Parse<PackageSourceKind>(row.SourceKind), row.InputDigest, row.ManifestJson,
            PackageValidationJson.Read<PackageReadLimits>(row.LimitsJson), row.CreatedUtc, row.IsOwnedUpload);
    }

    public async Task<PackageRunEnqueueResult> EnqueueAsync(PackageValidationRun run, CancellationToken cancellationToken = default)
    {
        if (run.Status != ValidationRunStatus.Pending || run.Revision != 0) throw new ArgumentException("Enqueue a new pending run.", nameof(run));
        Runs.Add(run);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(run.Copy(), true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_package_validation_runs_IdempotencyKey" })
        {
            var existing = await Runs.AsNoTracking().SingleAsync(row => row.IdempotencyKey == run.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing.RequestDigest != run.RequestDigest) throw new PackageValidationIdempotencyConflictException(run.IdempotencyKey);
            return new(existing, false);
        }
        finally { dbContext.Entry(run).State = EntityState.Detached; }
    }

    public Task<PackageValidationRun?> GetRunAsync(Guid id, CancellationToken cancellationToken = default) =>
        Runs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

    public async Task<PackageValidationPage<PackageRunSummary>> ListRunsAsync(Guid? applicationId, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        PackageValidationPaging.Validate(offset, limit);
        var query = Runs.AsNoTracking().Where(row => applicationId == null || row.ApplicationId == applicationId);
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        // Projection deliberately excludes request manifests and report/finding payloads.
        var rows = await query.OrderByDescending(row => row.CreatedUtc).ThenBy(row => row.Id).Skip(offset).Take(limit)
            .Select(row => new PackageRunSummary(row.Id, row.ApplicationId, row.SequenceNumber, row.Status, row.Stage,
                row.CreatedUtc, row.StartedUtc, row.CompletedUtc, row.AttemptCount, row.CancellationRequestedUtc != null,
                row.RequestDigest, row.Binding, row.FailureCode, row.FailureMessage, row.ReportSha256 != null, row.CoverageSummary))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(rows, count, offset, limit);
    }

    public async Task<PackageValidationRun?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await MutateAsync(id, run => run.RequestCancellation(nowUtc), cancellationToken).ConfigureAwait(false);
        return await GetRunAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageValidationLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = await Runs.AsNoTracking().Where(row => row.Status == ValidationRunStatus.Pending)
                .OrderBy(row => row.CreatedUtc).ThenBy(row => row.Id).Select(row => (Guid?)row.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (id is null) return null;
            var claimed = await MutateAsync(id.Value, run =>
            {
                if (run.Status != ValidationRunStatus.Pending) return false;
                run.Claim(owner, nowUtc, leaseDuration);
                return true;
            }, cancellationToken).ConfigureAwait(false);
            if (claimed is not null) return new(claimed, claimed.LeaseToken!.Value, owner);
        }
    }

    public async Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration,
        string stage, CancellationToken cancellationToken = default) =>
        await MutateAsync(id, run =>
        {
            if (!run.OwnsLease(token, owner, nowUtc)) return false;
            run.Heartbeat(token, owner, nowUtc, leaseDuration, stage);
            return true;
        }, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, ValidationRunStatus status,
        string code, string message, CancellationToken cancellationToken = default) =>
        await MutateAsync(id, run =>
        {
            if (!run.OwnsLease(token, owner, nowUtc)) return false;
            run.Fail(token, owner, nowUtc, status, code, message);
            return true;
        }, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<int> RecoverExpiredAsync(DateTime nowUtc, int maxAttempts, CancellationToken cancellationToken = default)
    {
        var ids = await Runs.AsNoTracking().Where(row => row.Status == ValidationRunStatus.Running && row.LeaseExpiresUtc <= nowUtc)
            .OrderBy(row => row.LeaseExpiresUtc).Select(row => row.Id).Take(100).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var recovered = 0;
        foreach (var id in ids)
            if (await MutateAsync(id, run => run.RecoverExpiredLease(nowUtc, maxAttempts), cancellationToken).ConfigureAwait(false) is not null) recovered++;
        return recovered;
    }

    public async Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, ValidationReport report,
        DossierProjection? projection = null, CancellationToken cancellationToken = default)
    {
        var json = PackageValidationJson.Write(report);
        var hash = PackageValidationJson.Hash(json);
        if (projection is not null && (projection.BaselineDigest != report.Binding.HistoryBaselineDigest ||
            projection.InputDigest != report.Binding.InputManifestDigest))
            throw new ArgumentException("An archived projection must bind the completed report's exact input and baseline.", nameof(projection));
        // Lock the run during final archival. Heartbeat/cancellation updates then
        // serialize against this commit instead of invalidating large finding batches.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var run = await Runs.FromSqlInterpolated($"SELECT * FROM package_validation_runs WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (run is null || !run.OwnsLease(token, owner, nowUtc) || run.CancellationRequestedUtc is not null) return false;
        dbContext.Attach(run);
        var archive = new PackageValidationReportRecord { RunId = id, Json = json };
        var projectionJson = projection is null ? null : PackageValidationJson.Write(projection);
        var projectionRecord = projectionJson is null ? null : new PackageValidationProjectionRecord
        {
            RunId = id, Json = projectionJson, Sha256 = PackageValidationJson.Hash(projectionJson)
        };
        var findings = report.Coverage.SelectMany(rule => rule.Findings).Select((finding, ordinal) =>
            new PackageValidationFindingRecord
            {
                RunId = id, Ordinal = ordinal, RuleId = finding.RuleId, Severity = finding.Severity.ToString(),
                Status = finding.CheckStatus.ToString(), LogicalPath = finding.Location.LogicalPath,
                NodePath = finding.Location.NodePath,
                Json = PackageValidationJson.Write(finding)
            }).ToArray();
        try
        {
            run.Complete(token, owner, nowUtc, report, hash);
            dbContext.Add(archive);
            if (projectionRecord is not null) dbContext.Add(projectionRecord);
            dbContext.AddRange(findings);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            dbContext.Entry(run).State = EntityState.Detached;
            dbContext.Entry(archive).State = EntityState.Detached;
            if (projectionRecord is not null) dbContext.Entry(projectionRecord).State = EntityState.Detached;
            foreach (var finding in findings) dbContext.Entry(finding).State = EntityState.Detached;
        }
    }

    public async Task<string?> GetReportJsonAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await GetRunAsync(id, cancellationToken).ConfigureAwait(false);
        if (run?.ReportSha256 is null) return null;
        var json = await dbContext.Set<PackageValidationReportRecord>().AsNoTracking().Where(row => row.RunId == id)
            .Select(row => row.Json).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The run's committed validation report is missing.");
        return PackageValidationJson.VerifyReport(json, run.ReportSha256, run.Binding);
    }

    public async Task<PackageValidationPage<ValidationFinding>> GetFindingsAsync(Guid id, PackageFindingQuery query,
        CancellationToken cancellationToken = default)
    {
        PackageValidationPaging.Validate(query.Offset, query.Limit);
        if (query.Severity is { } severity && !Enum.IsDefined(severity) || query.Status is { } status && !Enum.IsDefined(status))
            throw new ArgumentException("Unknown finding filter.", nameof(query));
        var severityText = query.Severity?.ToString();
        var statusText = query.Status?.ToString();
        var ruleIds = PackageFindingFilters.RuleIds(query.Category is null ? null : await GetRunAsync(id, cancellationToken).ConfigureAwait(false), query.Category);
        var rows = dbContext.Set<PackageValidationFindingRecord>().AsNoTracking().Where(row => row.RunId == id &&
            (query.RuleId == null || row.RuleId == query.RuleId) && (severityText == null || row.Severity == severityText) &&
            (statusText == null || row.Status == statusText) && (query.LogicalPath == null || row.LogicalPath == query.LogicalPath) &&
            (query.NodePath == null || row.NodePath != null && row.NodePath.Contains(query.NodePath)));
        if (ruleIds is not null) rows = rows.Where(row => ruleIds.Contains(row.RuleId));
        var count = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var json = await rows.OrderBy(row => row.Ordinal).Skip(query.Offset).Take(query.Limit).Select(row => row.Json)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(json.Select(PackageValidationJson.Read<ValidationFinding>).ToArray(), count, query.Offset, query.Limit);
    }

    public async Task<bool> RegisterExternalComparisonAsync(ExternalComparisonReport evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (await GetRunAsync(evidence.RunId, cancellationToken).ConfigureAwait(false) is not { Status: ValidationRunStatus.Completed, ReportSha256: not null } run)
            throw new InvalidOperationException("External evidence requires a completed validation run.");
        if (run.Binding != evidence.Binding)
            throw new InvalidOperationException("External evidence must bind the completed run exactly.");
        var existing = await dbContext.Set<PackageValidationExternalComparisonRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.RunId == evidence.RunId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Digest != evidence.Digest) throw new ExternalComparisonEvidenceConflictException(evidence.RunId);
            return false;
        }
        var record = new PackageValidationExternalComparisonRecord
        {
            RunId = evidence.RunId, Id = evidence.Id, ToolName = evidence.ToolName, ToolVersion = evidence.ToolVersion,
            PackageDigest = evidence.PackageDigest, ReportSha256 = evidence.ReportSha256, Digest = evidence.Digest,
            RecordedUtc = evidence.RecordedUtc, DifferencesJson = PackageValidationJson.Write(evidence.Differences),
            ReportText = evidence.ReportText
        };
        dbContext.Add(record);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            var committed = await dbContext.Set<PackageValidationExternalComparisonRecord>().AsNoTracking()
                .SingleAsync(row => row.RunId == evidence.RunId, cancellationToken).ConfigureAwait(false);
            if (committed.Digest != evidence.Digest) throw new ExternalComparisonEvidenceConflictException(evidence.RunId);
            return false;
        }
        finally { dbContext.Entry(record).State = EntityState.Detached; }
    }

    public async Task<string?> GetProjectionJsonAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Set<PackageValidationProjectionRecord>().AsNoTracking()
            .Where(item => item.RunId == id).Select(item => new { item.Json, item.Sha256 })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        if (PackageValidationJson.Hash(row.Json) != row.Sha256)
            throw new InvalidDataException("The archived projection digest differs from its committed hash.");
        return row.Json;
    }

    public async Task<ExternalComparisonEvidence?> GetExternalComparisonAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Set<PackageValidationExternalComparisonRecord>().AsNoTracking()
            .Where(row => row.RunId == runId)
            .Select(row => new { row.RunId, row.ToolName, row.ToolVersion, row.PackageDigest, row.ReportSha256,
                row.RecordedUtc, row.Digest, row.DifferencesJson })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : new(row.RunId, row.ToolName, row.ToolVersion, row.PackageDigest, row.ReportSha256,
            PackageValidationJson.Read<ExternalComparisonDifference[]>(row.DifferencesJson), row.RecordedUtc, row.Digest);
    }

    public Task<string?> GetExternalComparisonReportTextAsync(Guid runId, CancellationToken cancellationToken = default) =>
        dbContext.Set<PackageValidationExternalComparisonRecord>().AsNoTracking().Where(row => row.RunId == runId)
            .Select(row => row.ReportText).SingleOrDefaultAsync(cancellationToken)!;

    public Task<Guid?> FindCompletedRunAsync(Guid applicationId, string sequenceNumber, string inputManifestDigest,
        CancellationToken cancellationToken = default)
    {
        ValidationBinding.RequireDigest(inputManifestDigest);
        // Only a completed run with an archived report can authorize finalization.
        return Runs.AsNoTracking().Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber &&
                row.Status == ValidationRunStatus.Completed && row.ReportSha256 != null &&
                row.Binding.InputManifestDigest == inputManifestDigest)
            .OrderByDescending(row => row.CompletedUtc).ThenByDescending(row => row.Id)
            .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PackageValidationRun?> MutateAsync(Guid id, Func<PackageValidationRun, bool> mutation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var run = await GetRunAsync(id, cancellationToken).ConfigureAwait(false);
            if (run is null) return null;
            dbContext.Attach(run);
            try
            {
                if (!mutation(run)) return null;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return run;
            }
            catch (DbUpdateConcurrencyException) { }
            finally { dbContext.Entry(run).State = EntityState.Detached; }
        }
        throw new InvalidOperationException("Validation state could not acquire a stable revision.");
    }
}
