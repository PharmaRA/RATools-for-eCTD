using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Application.Abstractions.Security;
using RATools.Domain.PackageValidation;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;
using RATools.Domain.Standards;

namespace RATools.Application.PackageValidation;

public sealed record PackageHistorySelection(Guid InputId, string ProfileSnapshotId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StandardsSnapshotId = null);
public sealed record CreatePackageValidationRequest(Guid InputId, string ProfileSnapshotId,
    PackageValidationMode Mode, string IdempotencyKey, Guid? ComparisonInputId = null,
    IReadOnlyList<PackageHistorySelection>? History = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StandardsSnapshotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StandardsBusinessContext? StandardsBusinessContext = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? SelectionDate = null);
public sealed record StoredPackageValidationRequest(int SchemaVersion, Guid InputId, Guid? ComparisonInputId,
    IReadOnlyList<PackageHistorySelection> History, JsonElement RuleCatalog, JsonElement HistoryBaseline, PackageReadLimits ReadLimits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StandardsSnapshotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? SelectionDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StandardsBusinessContext? StandardsBusinessContext = null);

public sealed class PackageValidationService(IPackageValidationStore store, IPackageInputReader reader,
    IWorkspacePathPolicy pathPolicy, PackageDeliveryInspector inspector, TimeProvider? timeProvider = null,
    FinalizedHistoryResolver? finalizedHistory = null, StandardsSnapshotService? snapshots = null, IApplicationRepository? applications = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private TimeProvider Clock => timeProvider ?? TimeProvider.System;

    public async Task<PackageRunEnqueueResult> EnqueueAsync(CreatePackageValidationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var replay = await store.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return Replay(replay, request);
        var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        if (request.SelectionDate is not null && request.SelectionDate != today) throw new StandardsSelectionException(["SelectionDateInvalid: New runs must select today's standards policy."]);
        var catalog = PackageValidationCatalog.Current;
        StandardsProfileSnapshot? snapshot = null;
        if (snapshots is not null)
        {
            snapshot = request.StandardsSnapshotId is { } identity
                ? await snapshots.GetAsync(identity, cancellationToken).ConfigureAwait(false)
                : await snapshots.CaptureAsync(request.ProfileSnapshotId == PackageValidationCatalog.IchProfile ? RATools.Application.Applications.EctdTemplates.EctdTemplateRegistry.DefaultTemplateKey : catalog.Profiles[request.ProfileSnapshotId].TemplateKey, request.ProfileSnapshotId, cancellationToken).ConfigureAwait(false);
            if (snapshot.RuleProfileAlias != request.ProfileSnapshotId) throw new StandardsSelectionException(["RuleProfileMismatch: The exact snapshot has a different rule profile alias."]);
            catalog = PackageStandardsContext.FromSnapshot(snapshot).Catalog;
        }
        else if (request.Mode == PackageValidationMode.Formal || request.StandardsSnapshotId is not null)
            throw new StandardsSelectionException(["StandardsSnapshotMissing: This service cannot resolve exact standards evidence."]);
        _ = catalog.ForProfile(request.ProfileSnapshotId);
        var selected = await SelectAsync(request.InputId, request.ComparisonInputId, request.History ?? [], cancellationToken).ConfigureAwait(false);
        if (request.Mode == PackageValidationMode.Formal)
        {
            var reasons = await EvaluateAsync(snapshot, selected.Target.ApplicationId, request.StandardsBusinessContext, today, cancellationToken).ConfigureAwait(false);
            if (selected.History.Any(item => item.SnapshotId is null)) reasons = reasons.Concat(["HistoryStandardsMissing: Formal history requires each exact standards snapshot."]).ToArray();
            if (reasons.Count > 0) throw new StandardsSelectionException(reasons);
        }
        JsonElement ruleCatalog;
        if (snapshot is null) ruleCatalog = PackageValidationCatalog.ExportSnapshot();
        else { using var json = JsonDocument.Parse(snapshot.Resources.Single(resource => resource.Key == "rules").ReadBytes()); ruleCatalog = json.RootElement.Clone(); }
        var storedRequest = new StoredPackageValidationRequest(1, selected.Target.Id, selected.Comparison?.Id,
            selected.History.Select(pair => new PackageHistorySelection(pair.Input.Id, pair.Profile, pair.SnapshotId)).ToArray(), ruleCatalog,
            JsonSerializer.SerializeToElement(selected.Baseline, JsonOptions), selected.Target.Limits, snapshot?.Id, snapshot is null ? null : today, request.StandardsBusinessContext);
        var binding = new ValidationBinding(PackageDeliveryInput.ComputeDigest(selected.Target.InputDigest, selected.Comparison?.InputDigest),
            selected.Baseline.Digest, request.ProfileSnapshotId, catalog.Digest,
            inspector.EngineVersion, request.Mode, selected.Target.Limits.Digest(), snapshot?.Id, snapshot is null ? null : today, request.StandardsBusinessContext);
        var run = new PackageValidationRun(selected.Target.ApplicationId, selected.Target.SequenceNumber, binding,
            JsonSerializer.Serialize(storedRequest, JsonOptions), request.IdempotencyKey, Clock.GetUtcNow().UtcDateTime);
        try { return await store.EnqueueAsync(run, cancellationToken).ConfigureAwait(false); }
        catch (PackageValidationIdempotencyConflictException)
        {
            var winner = await store.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (winner is null) throw; return Replay(winner, request);
        }
    }

    // Each attempt reads fresh captures and verifies the full registration/request binding.
    // It never adopts partial parser output from an interrupted attempt. When a projection
    // observer is supplied it receives the cumulative view derived from the same facts.
    public Task<ValidationReport> InspectAsync(PackageValidationRun run, Func<string, Task>? stageChanged = null,
        CancellationToken cancellationToken = default) =>
        InspectAsync(run, stageChanged, null, cancellationToken);

    public async Task<ValidationReport> InspectAsync(PackageValidationRun run, Func<string, Task>? stageChanged,
        Func<DossierProjection, Task>? projectionReady, CancellationToken cancellationToken = default)
    {
        run.VerifyRequestIntegrity();
        if (run.Binding.EngineVersion != inspector.EngineVersion)
            throw new PackageValidationVersionChangedException();
        var request = JsonSerializer.Deserialize<StoredPackageValidationRequest>(run.RequestJson, JsonOptions)
            ?? throw new InvalidDataException("The validation request is missing.");
        if (request.SchemaVersion != 1) throw new InvalidDataException("Unsupported validation request schema.");
        if (RATools.Domain.Common.CanonicalJson.Digest(request.RuleCatalog) != run.Binding.RulesDigest)
            throw new InvalidDataException("The archived rule catalog differs from the queued binding.");
        if (request.StandardsSnapshotId != run.Binding.StandardsSnapshotId || request.SelectionDate != run.Binding.SelectionDate || request.StandardsBusinessContext != run.Binding.StandardsBusinessContext)
            throw new InvalidDataException("Archived standards selection differs from the run binding.");
        var context = await ContextAsync(run, request, cancellationToken).ConfigureAwait(false);
        var selected = await SelectAsync(request.InputId, request.ComparisonInputId, request.History, cancellationToken, request.HistoryBaseline).ConfigureAwait(false);
        if (request.ReadLimits.Digest() != run.Binding.ResourceLimitsDigest ||
            RATools.Domain.Common.CanonicalJson.Digest(request.HistoryBaseline) !=
            RATools.Domain.Common.CanonicalJson.Digest(JsonSerializer.SerializeToElement(selected.Baseline, JsonOptions)))
            throw new InvalidDataException("The archived history or resource limits differ from the selected request.");
        if (finalizedHistory is not null)
        {
            // A verified entry is evidence, so it must still be provable now: the snapshot
            // and the run it named must both exist and still bind these exact bytes. A
            // removed or replaced snapshot invalidates the run rather than downgrading the
            // claim silently.
            foreach (var entry in selected.Baseline.Entries)
            {
                if (!await finalizedHistory.VerifyAsync(entry, run.ApplicationId, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException(
                        $"The archived finalized history for sequence {entry.SequenceNumber} can no longer be verified.");
            }
        }
        var captures = new List<ICapturedPackageInput>();
        try
        {
            async Task<ICapturedPackageInput> Capture(RegisteredPackageInput input)
            {
                // Uploaded inputs have server-generated storage addresses. Directory and
                // registered ZIP paths are authorized again, including reparse-point checks.
                if (!input.IsOwnedUpload) pathPolicy.EnsureAllowed(input.SourcePath);
                ICapturedPackageInput captured;
                try
                {
                    captured = await reader.ReadAsync(new(input.ApplicationId, input.Id.ToString("D"), input.SourcePath,
                        input.SequenceNumber, input.RootRelativePath), input.Limits, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException exception) when (exception is not PackageInputException { RuleId: "INPUT-LIMITS" })
                { throw new PackageInputChangedException(input.Id.ToString("D")); }
                captures.Add(captured);
                if (captured.InputDigest != input.InputDigest || captured.SourceKind != input.SourceKind)
                    throw new PackageInputChangedException(input.Id.ToString("D"));
                return captured;
            }
            var target = await Capture(selected.Target).ConfigureAwait(false);
            var comparison = selected.Comparison is null ? null : await Capture(selected.Comparison).ConfigureAwait(false);
            var history = new List<ICapturedPackageInput>();
            foreach (var entry in selected.History) history.Add(await Capture(entry.Input).ConfigureAwait(false));
            var input = new PackageDeliveryInput(new(target, selected.Baseline, history), comparison);
            var binding = new ValidationBinding(input.InputDigest, selected.Baseline.Digest, run.Binding.ProfileSnapshotId,
                context.Catalog.Digest, inspector.EngineVersion, run.Binding.ValidationMode, target.Limits.Digest(), run.Binding.StandardsSnapshotId, run.Binding.SelectionDate, run.Binding.StandardsBusinessContext);
            if (binding != run.Binding || run.ApplicationId != target.Manifest.ApplicationId || run.SequenceNumber != target.Manifest.SequenceNumber)
                throw new PackageInputChangedException(target.SourceId);
            if (stageChanged is not null) await stageChanged("Inspecting").ConfigureAwait(false);
            var result = await inspector.WithStandards(context).InspectAsync(input, run.Binding.ProfileSnapshotId, cancellationToken).ConfigureAwait(false);
            if (projectionReady is not null)
                await projectionReady(DossierProjectionBuilder.Build(result.Lifecycle, input)).ConfigureAwait(false);
            IReadOnlyList<string> standardsReasons = run.Binding.StandardsSnapshotId is null ? ["StandardsSnapshotMissing: Legacy validation carries no exact standards evidence."] :
                await EvaluateAsync(await snapshots!.GetAsync(run.Binding.StandardsSnapshotId, cancellationToken).ConfigureAwait(false), run.ApplicationId,
                    run.Binding.StandardsBusinessContext, run.Binding.SelectionDate!.Value, cancellationToken).ConfigureAwait(false);
            if (request.History.Any(item => item.StandardsSnapshotId is null)) standardsReasons = standardsReasons.Concat(["HistoryStandardsMissing: Legacy historical selection has no exact standards snapshot."]).ToArray();
            var report = result.CreateReport(run.Binding.ValidationMode, run.Binding, standardsReasons);
            if (report.Binding != run.Binding) throw new PackageValidationVersionChangedException();
            return report;
        }
        finally
        {
            // Disposal only removes owned attempt captures; original and uploaded inputs remain.
            foreach (var capture in captures) await capture.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Selection> SelectAsync(Guid targetId, Guid? comparisonId, IReadOnlyList<PackageHistorySelection> history,
        CancellationToken cancellationToken, JsonElement? archivedBaseline = null)
    {
        if (history.Count > 1000) throw new ArgumentException("Select at most 1000 historical sequences.", nameof(history));
        var target = await RequiredInput(targetId).ConfigureAwait(false);
        var comparison = comparisonId is { } id ? await RequiredInput(id).ConfigureAwait(false) : null;
        if (comparison is not null && (comparison.ApplicationId != target.ApplicationId || comparison.SequenceNumber != target.SequenceNumber ||
            comparison.SourceKind == target.SourceKind || comparison.Limits.Digest() != target.Limits.Digest()))
            throw new ArgumentException("Comparison requires opposite directory/ZIP kinds with matching application, sequence and limits.");
        var selected = new List<(RegisteredPackageInput Input, string Profile, string? SnapshotId)>();
        foreach (var entry in history)
        {
            if (entry is null) throw new ArgumentException("Historical selections cannot contain null entries.", nameof(history));
            var input = await RequiredInput(entry.InputId).ConfigureAwait(false);
            if (entry.StandardsSnapshotId is not null)
            {
                if (snapshots is null) throw new InvalidDataException("An exact historical standards selection cannot be resolved.");
                var snapshot = await snapshots.GetAsync(entry.StandardsSnapshotId, cancellationToken).ConfigureAwait(false);
                if (snapshot.RuleProfileAlias != entry.ProfileSnapshotId) throw new InvalidDataException("Historical standards alias and content identity disagree.");
            }
            else PackageValidationCatalog.Current.ForProfile(entry.ProfileSnapshotId);
            if (input.ApplicationId != target.ApplicationId || input.Limits.Digest() != target.Limits.Digest())
                throw new ArgumentException("Historical inputs must belong to the same application and use the same resource limits.");
            selected.Add((input, entry.ProfileSnapshotId, entry.StandardsSnapshotId));
        }
        selected.Sort((left, right) => string.CompareOrdinal(left.Input.SequenceNumber, right.Input.SequenceNumber));
        if (archivedBaseline is { } archived)
        {
            if (archived.GetProperty("applicationId").GetGuid() != target.ApplicationId || archived.GetProperty("targetSequence").GetString() != target.SequenceNumber)
                throw new InvalidDataException("The archived history scope differs from the selected input.");
            var originalEntries = archived.GetProperty("entries").EnumerateArray().Select(entry => entry.Deserialize<HistoryBaselineEntry>(JsonOptions) ?? throw new InvalidDataException()).ToArray();
            var original = new HistoryBaselineManifest(target.ApplicationId, target.SequenceNumber, originalEntries);
            if (original.Digest != archived.GetProperty("digest").GetString() || original.Entries.Count != selected.Count)
                throw new InvalidDataException("The archived history baseline failed integrity verification.");
            foreach (var entry in original.Entries)
            {
                var match = selected.SingleOrDefault(item => item.Input.SequenceNumber == entry.SequenceNumber);
                if (match.Input is null || match.Input.Id.ToString("D") != entry.SourceId || match.Input.InputDigest != entry.ContentDigest ||
                    match.Profile != entry.ProfileSnapshotId || match.SnapshotId != entry.StandardsSnapshotId)
                    throw new InvalidDataException("An archived historical input differs from its selected immutable binding.");
            }
            return new(target, comparison, selected, original);
        }
        // The external-input API cannot assert trust: a verified entry is derived only from
        // a finalized snapshot that recorded exactly these bytes, and carries that snapshot
        // and run so the claim can be re-proved later.
        var entries = new List<HistoryBaselineEntry>(selected.Count);
        foreach (var (input, profile, snapshotId) in selected)
        {
            var finalized = finalizedHistory is null
                ? null
                : await finalizedHistory.ResolveAsync(target.ApplicationId, input.SequenceNumber, input.InputDigest,
                    cancellationToken).ConfigureAwait(false);
            if (finalized is not null && snapshotId is not null)
            {
                var evidenceRun = await store.GetRunAsync(finalized.ValidationRunId, cancellationToken).ConfigureAwait(false);
                if (evidenceRun?.Binding.StandardsSnapshotId != snapshotId || evidenceRun.Binding.ProfileSnapshotId != profile) finalized = null;
            }
            // SourceId always names the captured input the baseline was built from; the
            // finalized snapshot and run are recorded separately as the trust evidence.
            entries.Add(finalized is null || snapshotId is null && snapshots is not null
                ? new HistoryBaselineEntry(input.SequenceNumber, HistorySourceKind.ImportedExternal, input.Id.ToString("D"),
                    input.InputDigest, profile, HistoryTrustStatus.Unverified, StandardsSnapshotId: snapshotId)
                : new HistoryBaselineEntry(input.SequenceNumber, HistorySourceKind.Finalized, input.Id.ToString("D"),
                    input.InputDigest, profile, HistoryTrustStatus.VerifiedForScope, finalized.ValidationRunId,
                    finalized.FinalizedSequenceId, snapshotId));
        }
        var baseline = new HistoryBaselineManifest(target.ApplicationId, target.SequenceNumber, entries);
        return new(target, comparison, selected, baseline);

        async Task<RegisteredPackageInput> RequiredInput(Guid id) => await store.GetInputAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Validation input '{id}' does not exist.");
    }

    private sealed record Selection(RegisteredPackageInput Target, RegisteredPackageInput? Comparison,
        List<(RegisteredPackageInput Input, string Profile, string? SnapshotId)> History, HistoryBaselineManifest Baseline);

    private static PackageRunEnqueueResult Replay(PackageValidationRun run, CreatePackageValidationRequest request)
    {
        run.VerifyRequestIntegrity(); var stored = JsonSerializer.Deserialize<StoredPackageValidationRequest>(run.RequestJson, JsonOptions) ?? throw new InvalidDataException();
        var requestedHistory = (request.History ?? []).OrderBy(item => item.InputId).ToArray(); var previousHistory = stored.History.OrderBy(item => item.InputId).ToArray();
        if (stored.InputId != request.InputId || stored.ComparisonInputId != request.ComparisonInputId || run.Binding.ProfileSnapshotId != request.ProfileSnapshotId ||
            run.Binding.ValidationMode != request.Mode || request.StandardsSnapshotId is not null && request.StandardsSnapshotId != run.Binding.StandardsSnapshotId ||
            request.SelectionDate is not null && request.SelectionDate != run.Binding.SelectionDate || request.StandardsBusinessContext != run.Binding.StandardsBusinessContext ||
            !requestedHistory.SequenceEqual(previousHistory)) throw new PackageValidationIdempotencyConflictException(request.IdempotencyKey);
        return new(run, false);
    }
    private async Task<PackageStandardsContext> ContextAsync(PackageValidationRun run, StoredPackageValidationRequest request, CancellationToken cancellationToken)
    {
        if (run.Binding.StandardsSnapshotId is null)
        {
            if (run.Binding.ValidationMode == PackageValidationMode.Formal) throw new StandardsSelectionException(["StandardsSnapshotMissing: A legacy alias cannot authorize a new formal run."]);
            if (run.Binding.RulesDigest != PackageValidationCatalog.Current.Digest) throw new PackageValidationVersionChangedException();
            return PackageStandardsContext.Current;
        }
        if (snapshots is null) throw new InvalidDataException("Exact standards evidence is unavailable.");
        var snapshot = await snapshots.GetAsync(run.Binding.StandardsSnapshotId, cancellationToken).ConfigureAwait(false);
        var history = new Dictionary<string, PackageStandardsContext>(StringComparer.Ordinal);
        foreach (var entry in request.History)
        {
            var input = await store.GetInputAsync(entry.InputId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException();
            history[input.SequenceNumber] = entry.StandardsSnapshotId is { } id ? PackageStandardsContext.FromSnapshot(await snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false), selectionDate: run.Binding.SelectionDate) : PackageStandardsContext.Current;
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, history, run.Binding.SelectionDate);
        if (context.Catalog.Digest != run.Binding.RulesDigest || snapshot.RuleProfileAlias != run.Binding.ProfileSnapshotId) throw new InvalidDataException("The exact standards snapshot differs from the run rule binding.");
        return context;
    }
    private async Task<IReadOnlyList<string>> EvaluateAsync(StandardsProfileSnapshot? snapshot, Guid appId, StandardsBusinessContext? context, DateOnly date, CancellationToken cancellationToken)
    {
        if (snapshot is null) return ["StandardsSnapshotMissing: Exact standards evidence is required."];
        var app = applications is null ? null : await applications.GetAsync(appId, cancellationToken).ConfigureAwait(false);
        var reasons = snapshot.Evaluate(context, date, app?.EctdTemplateKey ?? snapshot.TemplateKey).ToList();
        if (app is null) reasons.Add("ApplicationContextMissing: The declared scope cannot be bound to an application template.");
        if (context is not null) reasons.Add("RegionalBusinessContextUnconfirmed: Exact selected regional metadata and complete regional criteria/external evidence remain unqualified for Formal admission.");
        return reasons.AsReadOnly();
    }
}

public sealed class PackageValidationVersionChangedException()
    : InvalidOperationException("The current engine/rule versions differ from this queued request; create a new validation run.");
