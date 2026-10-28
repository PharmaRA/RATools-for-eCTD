using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Finalization;

// Captures the validated workspace as an immutable release before a snapshot is
// registered. Every refusal happens before anything is written, and the provider verifies
// the archived run, report and captured input itself, so a snapshot cannot be built from a
// run the caller merely named.
public sealed class ReleaseCaptureService(
    IPackageValidationStore validationStore,
    IReleaseCapturePlanner planner,
    IReleaseCaptureStore store) : IFinalizationCaptureProvider
{
    public async Task<IFinalizationCapture> CaptureAsync(FinalizeSequenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sequenceNumber = request.SequenceNumber.Trim();
        var run = await validationStore.GetRunAsync(request.PackageValidationRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinalizationNotAuthorizedException("The referenced package validation run does not exist.");
        if (run.ApplicationId != request.ApplicationId ||
            !string.Equals(run.SequenceNumber, sequenceNumber, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The referenced validation run belongs to a different application or sequence.");
        if (run.Status != ValidationRunStatus.Completed || run.ReportSha256 is null)
            throw new FinalizationNotAuthorizedException("Only a completed validation run can be finalized.");
        if (!string.Equals(run.Binding.ProfileSnapshotId, request.StandardsProfileSnapshotId.Trim(), StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The requested standards snapshot is not the profile validated by this run.");
        if (!string.Equals(run.Binding.HistoryBaselineDigest, request.HistoryBaselineDigest, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The requested history baseline is not the baseline validated by this run.");
        var reportJson = await validationStore.GetReportJsonAsync(run.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new FinalizationNotAuthorizedException("The validation run has no readable archived report.");
        var reportBytes = Encoding.UTF8.GetBytes(reportJson);
        // The report bytes are the readiness evidence; they must still hash to the run's
        // committed digest before anything is captured from them.
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(reportBytes)).ToLowerInvariant(),
                run.ReportSha256, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The archived report bytes do not match the run's committed hash.");
        var (ready, reasons) = ReadReadiness(reportJson);
        if (run.Binding.ValidationMode == PackageValidationMode.Formal && !ready)
            throw new FinalizationNotAuthorizedException(
                "A formal finalization requires a report that is ready for finalization: " + string.Join(" | ", reasons));

        var stored = StoredPackageValidationRequestReader.Read(run);
        var input = await validationStore.GetInputAsync(stored.InputId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinalizationNotAuthorizedException("The validation run's captured input no longer exists.");
        var validatedDocuments = ReadValidatedDocuments(input.ManifestJson);

        var preparation = await planner.PlanAsync(request, run, stored, cancellationToken).ConfigureAwait(false);
        // Only files the run actually validated may be delivered. The workspace could have
        // changed between validation and this finalization; delivering the newer bytes
        // would claim validation evidence for content that was never inspected.
        VerifyDocuments(preparation, validatedDocuments);

        var files = preparation.Capture.Files
            .Append(new ReleaseCaptureFile("validation-report.json", ReleaseFileKind.ValidationReport, null,
                reportBytes, reportBytes.Length, run.ReportSha256,
                Convert.ToHexString(MD5.HashData(reportBytes)).ToLowerInvariant()))
            .ToArray();
        var manifest = new ReleaseManifest(preparation.Capture.ApplicationId, sequenceNumber, request.ExpectedRevision,
            FinalizationService.ToolVersion, run.Binding.InputManifestDigest, preparation.HistoryBaseline,
            run.Binding.HistoryBaselineDigest, preparation.Validation, preparation.Assets,
            files.Select(file => new ReleaseManifestFile(file.RelativePath, file.Length, file.Sha256, file.Md5, file.Kind)),
            preparation.Lifecycle, preparation.Leaves, preparation.Nodes);
        var staging = await store.SaveAsync(preparation.Capture with { Files = files }, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await store.CompleteAsync(staging, manifest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await store.DiscardAsync(staging, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return new FinalizationCapture(store, staging, manifest, run.Id, run.ReportSha256, ready, reasons);
    }

    public async Task<ReleaseManifest> ReadManifestAsync(FinalizedSequence snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return await store.ReadManifestAsync(snapshot.ApplicationId, snapshot.SequenceNumber, snapshot.ManifestDigest,
            cancellationToken).ConfigureAwait(false)
            ?? throw new ReleaseCaptureMissingException(snapshot.ManifestDigest);
    }

    public async Task<ReleaseFileContent> OpenFileAsync(FinalizedSequence snapshot, string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return await store.OpenFileAsync(snapshot.ApplicationId, snapshot.SequenceNumber, snapshot.ManifestDigest,
            relativePath, cancellationToken).ConfigureAwait(false);
    }

    private static (bool Ready, IReadOnlyList<string> Reasons) ReadReadiness(string reportJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !string.Equals(root.GetProperty("runStatus").GetString(), "Completed", StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The archived report is not a completed schema-1 report.");
        var reasons = root.GetProperty("blockingReasons").EnumerateArray()
            .Select(reason => reason.GetString() ?? string.Empty).ToArray();
        return (root.GetProperty("isReadyForFinalization").GetBoolean(), reasons);
    }

    // The validation input's own manifest names the exact bytes that were inspected. Only
    // exact manifest members are addressed; nothing is resolved against the workspace.
    private static Dictionary<string, (long Length, string Sha256)> ReadValidatedDocuments(string manifestJson)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var files = document.RootElement.GetProperty("files");
        var result = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
        foreach (var file in files.EnumerateArray())
        {
            var path = file.GetProperty("logicalPath").GetString()
                ?? throw new InvalidDataException("A validated input file has no logical path.");
            if (!result.TryAdd(path, (file.GetProperty("length").GetInt64(), file.GetProperty("sha256").GetString()!)))
                throw new InvalidDataException($"The validated input lists '{path}' twice.");
        }
        return result;
    }

    private static void VerifyDocuments(ReleaseCapturePreparation preparation,
        Dictionary<string, (long Length, string Sha256)> validatedDocuments)
    {
        foreach (var (logicalPath, sha256) in preparation.ValidatedDocuments)
        {
            if (!validatedDocuments.TryGetValue(logicalPath, out var validated) ||
                !string.Equals(validated.Sha256, sha256, StringComparison.Ordinal))
                throw new FinalizationNotAuthorizedException(
                    $"Document '{logicalPath}' is not the bytes this validation run captured; the workspace changed after validation.");
        }
    }
}

// One capture in progress. PublishAsync runs immediately before registration so a
// registered snapshot always has its bytes; a registration failure discards only a
// directory this capture created.
public sealed class FinalizationCapture(IReleaseCaptureStore store, ReleaseCaptureStaging staging,
    ReleaseManifest manifest, Guid validationRunId, string reportSha256, bool reportReady,
    IReadOnlyList<string> blockingReasons) : IFinalizationCapture
{
    private bool _published;
    private bool _created;

    public string ManifestDigest => manifest.Digest;
    public ReleaseManifest Manifest => manifest;
    public Guid ValidationRunId => validationRunId;
    public string ValidationInputManifestDigest => manifest.ValidatedInputManifestDigest;
    public string ReportSha256 => reportSha256;
    public PackageValidationMode ValidationMode => Enum.Parse<PackageValidationMode>(manifest.Validation.ValidationMode);
    public bool ReportReady => reportReady;
    public IReadOnlyList<string> BlockingReasons => blockingReasons;

    public async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        if (_published) throw new InvalidOperationException("The capture was already published.");
        var result = await store.CommitAsync(staging, manifest.Digest, cancellationToken).ConfigureAwait(false);
        _created = result.Created;
        _published = true;
    }

    public async Task DiscardAsync(bool removePublishedDirectory = false)
    {
        if (!_published)
        {
            await store.DiscardAsync(staging, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        // A directory that already held identical content belonged to an earlier snapshot;
        // only a directory this capture created may be removed, and only when the caller
        // proved no snapshot references it.
        if (_created && removePublishedDirectory)
            await store.DiscardCommittedAsync(staging.ApplicationId, staging.SequenceNumber, manifest.Digest,
                CancellationToken.None).ConfigureAwait(false);
    }
}
