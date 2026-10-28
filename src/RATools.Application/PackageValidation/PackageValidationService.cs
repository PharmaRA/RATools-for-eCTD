using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Application.Abstractions.Security;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record PackageHistorySelection(Guid InputId, string ProfileSnapshotId);
public sealed record CreatePackageValidationRequest(Guid InputId, string ProfileSnapshotId,
    PackageValidationMode Mode, string IdempotencyKey, Guid? ComparisonInputId = null,
    IReadOnlyList<PackageHistorySelection>? History = null);
public sealed record StoredPackageValidationRequest(int SchemaVersion, Guid InputId, Guid? ComparisonInputId,
    IReadOnlyList<PackageHistorySelection> History, JsonElement RuleCatalog, JsonElement HistoryBaseline, PackageReadLimits ReadLimits);

public sealed class PackageValidationService(IPackageValidationStore store, IPackageInputReader reader,
    IWorkspacePathPolicy pathPolicy, PackageDeliveryInspector inspector, TimeProvider? timeProvider = null,
    FinalizedHistoryResolver? finalizedHistory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private TimeProvider Clock => timeProvider ?? TimeProvider.System;

    public async Task<PackageRunEnqueueResult> EnqueueAsync(CreatePackageValidationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackageValidationCatalog.Current.ForProfile(request.ProfileSnapshotId);
        var selected = await SelectAsync(request.InputId, request.ComparisonInputId, request.History ?? [], cancellationToken).ConfigureAwait(false);
        var storedRequest = new StoredPackageValidationRequest(1, selected.Target.Id, selected.Comparison?.Id,
            selected.History.Select(pair => new PackageHistorySelection(pair.Input.Id, pair.Profile)).ToArray(), PackageValidationCatalog.ExportSnapshot(),
            JsonSerializer.SerializeToElement(selected.Baseline, JsonOptions), selected.Target.Limits);
        var binding = new ValidationBinding(PackageDeliveryInput.ComputeDigest(selected.Target.InputDigest, selected.Comparison?.InputDigest),
            selected.Baseline.Digest, request.ProfileSnapshotId, PackageValidationCatalog.Current.Digest,
            inspector.EngineVersion, request.Mode, selected.Target.Limits.Digest());
        var run = new PackageValidationRun(selected.Target.ApplicationId, selected.Target.SequenceNumber, binding,
            JsonSerializer.Serialize(storedRequest, JsonOptions), request.IdempotencyKey, Clock.GetUtcNow().UtcDateTime);
        return await store.EnqueueAsync(run, cancellationToken).ConfigureAwait(false);
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
        if (run.Binding.RulesDigest != PackageValidationCatalog.Current.Digest || run.Binding.EngineVersion != inspector.EngineVersion)
            throw new PackageValidationVersionChangedException();
        var request = JsonSerializer.Deserialize<StoredPackageValidationRequest>(run.RequestJson, JsonOptions)
            ?? throw new InvalidDataException("The validation request is missing.");
        if (request.SchemaVersion != 1) throw new InvalidDataException("Unsupported validation request schema.");
        if (RATools.Domain.Common.CanonicalJson.Digest(request.RuleCatalog) != run.Binding.RulesDigest)
            throw new InvalidDataException("The archived rule catalog differs from the queued binding.");
        var selected = await SelectAsync(request.InputId, request.ComparisonInputId, request.History, cancellationToken).ConfigureAwait(false);
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
                PackageValidationCatalog.Current.Digest, inspector.EngineVersion, run.Binding.ValidationMode, target.Limits.Digest());
            if (binding != run.Binding || run.ApplicationId != target.Manifest.ApplicationId || run.SequenceNumber != target.Manifest.SequenceNumber)
                throw new PackageInputChangedException(target.SourceId);
            if (stageChanged is not null) await stageChanged("Inspecting").ConfigureAwait(false);
            var result = await inspector.InspectAsync(input, run.Binding.ProfileSnapshotId, cancellationToken).ConfigureAwait(false);
            if (projectionReady is not null)
                await projectionReady(DossierProjectionBuilder.Build(result.Lifecycle, input)).ConfigureAwait(false);
            var report = result.CreateReport(run.Binding.ValidationMode);
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
        CancellationToken cancellationToken)
    {
        if (history.Count > 1000) throw new ArgumentException("Select at most 1000 historical sequences.", nameof(history));
        var target = await RequiredInput(targetId).ConfigureAwait(false);
        var comparison = comparisonId is { } id ? await RequiredInput(id).ConfigureAwait(false) : null;
        if (comparison is not null && (comparison.ApplicationId != target.ApplicationId || comparison.SequenceNumber != target.SequenceNumber ||
            comparison.SourceKind == target.SourceKind || comparison.Limits.Digest() != target.Limits.Digest()))
            throw new ArgumentException("Comparison requires opposite directory/ZIP kinds with matching application, sequence and limits.");
        var selected = new List<(RegisteredPackageInput Input, string Profile)>();
        foreach (var entry in history)
        {
            if (entry is null) throw new ArgumentException("Historical selections cannot contain null entries.", nameof(history));
            var input = await RequiredInput(entry.InputId).ConfigureAwait(false);
            PackageValidationCatalog.Current.ForProfile(entry.ProfileSnapshotId);
            if (input.ApplicationId != target.ApplicationId || input.Limits.Digest() != target.Limits.Digest())
                throw new ArgumentException("Historical inputs must belong to the same application and use the same resource limits.");
            selected.Add((input, entry.ProfileSnapshotId));
        }
        selected.Sort((left, right) => string.CompareOrdinal(left.Input.SequenceNumber, right.Input.SequenceNumber));
        // The external-input API cannot assert trust: a verified entry is derived only from
        // a finalized snapshot that recorded exactly these bytes, and carries that snapshot
        // and run so the claim can be re-proved later.
        var entries = new List<HistoryBaselineEntry>(selected.Count);
        foreach (var (input, profile) in selected)
        {
            var finalized = finalizedHistory is null
                ? null
                : await finalizedHistory.ResolveAsync(target.ApplicationId, input.SequenceNumber, input.InputDigest,
                    cancellationToken).ConfigureAwait(false);
            // SourceId always names the captured input the baseline was built from; the
            // finalized snapshot and run are recorded separately as the trust evidence.
            entries.Add(finalized is null
                ? new HistoryBaselineEntry(input.SequenceNumber, HistorySourceKind.ImportedExternal, input.Id.ToString("D"),
                    input.InputDigest, profile, HistoryTrustStatus.Unverified)
                : new HistoryBaselineEntry(input.SequenceNumber, HistorySourceKind.Finalized, input.Id.ToString("D"),
                    input.InputDigest, profile, HistoryTrustStatus.VerifiedForScope, finalized.ValidationRunId,
                    finalized.FinalizedSequenceId));
        }
        var baseline = new HistoryBaselineManifest(target.ApplicationId, target.SequenceNumber, entries);
        return new(target, comparison, selected, baseline);

        async Task<RegisteredPackageInput> RequiredInput(Guid id) => await store.GetInputAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Validation input '{id}' does not exist.");
    }

    private sealed record Selection(RegisteredPackageInput Target, RegisteredPackageInput? Comparison,
        List<(RegisteredPackageInput Input, string Profile)> History, HistoryBaselineManifest Baseline);
}

public sealed class PackageValidationVersionChangedException()
    : InvalidOperationException("The current engine/rule versions differ from this queued request; create a new validation run.");
