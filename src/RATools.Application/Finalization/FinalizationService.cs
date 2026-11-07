using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Finalization;

// Finalization records the identity of content that was actually validated. It captures
// the workspace bytes itself, verifies the completed run, its archived report and its
// captured input against those bytes, and archives the report's readiness verbatim. The
// manifest digest is derived from the capture - never accepted from the caller - so a
// snapshot cannot claim content that was not delivered.
public sealed class FinalizationService(
    IFinalizationStore store,
    IFinalizationCaptureProvider captures,
    IWorkspaceRevisionStore revisions)
{
    // The tool version participates in the snapshot identity. Bump it whenever the
    // meaning of a captured manifest changes so old snapshots stay interpretable.
    public const string ToolVersion = "ratools-finalization-v3";

    public async Task<FinalizationResult> FinalizeAsync(FinalizeSequenceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sequenceNumber = request.SequenceNumber.Trim();
        var existing = await store.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return Replay(existing, request);

        // Fail before copying gigabytes: a request that already names a stale revision is
        // decided by the cheap check, not by reading the whole workspace.
        var startingRevision = await revisions.GetRevisionAsync(request.ApplicationId, sequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (startingRevision is null)
            throw new FinalizationNotFoundException(
                $"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}.");
        if (startingRevision != request.ExpectedRevision)
            throw new WorkspaceRevisionConflictException(request.ExpectedRevision, startingRevision.Value);

        // The capture reads the workspace under the same application lock writers use and
        // re-hashes every byte it writes. It must not run inside the lock below: the package
        // model builder takes that lock itself.
        var capture = await captures.CaptureAsync(request with { SequenceNumber = sequenceNumber }, cancellationToken)
            .ConfigureAwait(false);
        // The registration decision runs under the lock: the revision the operator saw must
        // still be current, so no edit can slip between the capture and the commit. A late
        // edit is detected here because revisions only move forward. Acquiring the lock is
        // inside the try so a cancelled or failed acquisition still discards the staging
        // bytes this request created.
        IAsyncDisposable? heldLock = null;
        try
        {
            heldLock = await revisions.LockApplicationAsync(request.ApplicationId, cancellationToken).ConfigureAwait(false);
            var currentRevision = await revisions.GetRevisionAsync(request.ApplicationId, sequenceNumber, cancellationToken)
                .ConfigureAwait(false);
            if (currentRevision is null)
                throw new FinalizationNotFoundException(
                    $"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}.");
            if (currentRevision != request.ExpectedRevision)
                throw new WorkspaceRevisionConflictException(request.ExpectedRevision, currentRevision.Value);

            var sequence = FinalizedSequence.Create(request.ApplicationId, sequenceNumber, request.ExpectedRevision,
                capture.ManifestDigest, request.StandardsProfileSnapshotId, request.IchEctdVersion,
                request.RegionalModule1Version, request.HistoryBaselineDigest, capture.ValidationRunId,
                capture.ValidationInputManifestDigest, capture.ReportSha256, capture.ValidationMode,
                capture.ReportReady, capture.BlockingReasons, ToolVersion, request.IdempotencyKey, DateTime.UtcNow);
            // Publish before registration: a registered snapshot must always have its bytes.
            await capture.PublishAsync(cancellationToken).ConfigureAwait(false);
            var registered = await store.RegisterAsync(sequence, cancellationToken).ConfigureAwait(false);
            return new FinalizationResult(registered.Sequence, registered.Created);
        }
        catch
        {
            // Cleanup must not be skipped because the request was cancelled: the release is
            // shared evidence as soon as any snapshot records its digest - including one a
            // concurrent request registered while this one held the lock.
            var referenced = await IsReferencedAsync(request.ApplicationId, sequenceNumber, capture.ManifestDigest)
                .ConfigureAwait(false);
            await capture.DiscardAsync(removePublishedDirectory: !referenced).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (heldLock is not null) await heldLock.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Read under the application lock on the failure path, so a snapshot committed by a
    // concurrent request for the same digest is visible before any directory is removed.
    // Deliberately not cancellable: this decides whether cleanup may delete committed
    // evidence, and a cancelled read must not answer "no".
    private async Task<bool> IsReferencedAsync(Guid applicationId, string sequenceNumber, string manifestDigest)
    {
        var snapshots = await store.ListBySequenceAsync(applicationId, sequenceNumber, CancellationToken.None)
            .ConfigureAwait(false);
        return snapshots.Any(snapshot => string.Equals(snapshot.ManifestDigest, manifestDigest, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<FinalizedSequence>> ListAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default) =>
        await store.ListBySequenceAsync(applicationId, sequenceNumber.Trim(), cancellationToken).ConfigureAwait(false);

    public async Task<FinalizedSequence> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await store.GetAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new FinalizationNotFoundException($"Finalized sequence {id} was not found.");

    // The captured manifest is read from disk and re-verified against the snapshot's
    // recorded digest. A snapshot whose bytes are missing or edited is an error, never a
    // silently partial answer.
    public async Task<ReleaseManifest> GetManifestAsync(Guid finalizedSequenceId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(finalizedSequenceId, cancellationToken).ConfigureAwait(false);
        return await captures.ReadManifestAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    // The immutable content address for a preview of one delivered file. The reader gets
    // the captured release member, not the document's current workspace path, so an old
    // snapshot keeps showing the bytes it actually delivered.
    public async Task<ReleaseFileContent> GetReleaseFileAsync(Guid finalizedSequenceId, string relativePath,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(finalizedSequenceId, cancellationToken).ConfigureAwait(false);
        return await captures.OpenFileAsync(snapshot, relativePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeliveryRecord> RegisterDeliveryAsync(Guid finalizedSequenceId, string packageSha256, string channel,
        string? externalReference, string? note, string recordedBy, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(finalizedSequenceId, cancellationToken).ConfigureAwait(false);
        if (!snapshot.IsReadyForFinalization || snapshot.ValidationMode != PackageValidationMode.Formal)
            throw new FinalizationNotAuthorizedException(
                "Delivery facts cannot be recorded against a development or non-ready snapshot.");
        ValidationBinding.RequireDigest(packageSha256);
        ReleaseFileContent package;
        try
        {
            package = await captures.OpenFileAsync(snapshot, "delivery-package.zip", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ReleaseFileNotFoundException or ReleaseCaptureMissingException)
        {
            throw new FinalizationNotAuthorizedException("The snapshot has no manifest-bound delivery ZIP; delivery evidence cannot be registered.");
        }
        await using var verifiedPackage = package;
        if (!string.Equals(packageSha256, package.Sha256, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The delivery SHA-256 does not match the immutable captured ZIP.");
        var record = DeliveryRecord.Create(finalizedSequenceId, packageSha256, channel, externalReference, note, recordedBy,
            DateTime.UtcNow);
        await store.AppendDeliveryAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<DeliveryReceipt> RegisterReceiptAsync(Guid deliveryRecordId, DeliveryReceiptKind kind, string outcome,
        string rawText, string? externalReference, string recordedBy, CancellationToken cancellationToken = default)
    {
        if (deliveryRecordId == Guid.Empty) throw new ArgumentException("A delivery record is required.", nameof(deliveryRecordId));
        if (!await store.DeliveryExistsAsync(deliveryRecordId, cancellationToken).ConfigureAwait(false))
            throw new FinalizationNotFoundException($"Delivery record {deliveryRecordId} was not found.");
        var receipt = DeliveryReceipt.Create(deliveryRecordId, kind, outcome, rawText, externalReference, recordedBy,
            DateTime.UtcNow);
        await store.AppendReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    // Replaying an idempotency key returns the committed snapshot only when every request
    // claim matches; a changed request must never silently reuse old content.
    private static FinalizationResult Replay(FinalizedSequence existing, FinalizeSequenceRequest request)
    {
        if (existing.ApplicationId != request.ApplicationId ||
            !string.Equals(existing.SequenceNumber, request.SequenceNumber.Trim(), StringComparison.Ordinal) ||
            existing.SourceWorkspaceRevision != request.ExpectedRevision ||
            !string.Equals(existing.StandardsProfileSnapshotId, request.StandardsProfileSnapshotId.Trim(), StringComparison.Ordinal) ||
            // The stored value is trimmed; compare the normalized request so surrounding
            // whitespace cannot turn an identical retry into a conflict.
            !string.Equals(existing.IchEctdVersion, request.IchEctdVersion.Trim(), StringComparison.Ordinal) ||
            !string.Equals(existing.RegionalModule1Version, string.IsNullOrWhiteSpace(request.RegionalModule1Version)
                ? null : request.RegionalModule1Version.Trim(), StringComparison.Ordinal) ||
            !string.Equals(existing.HistoryBaselineDigest, request.HistoryBaselineDigest, StringComparison.Ordinal) ||
            existing.ValidationRunId != request.PackageValidationRunId)
            throw new FinalizationIdempotencyConflictException(request.IdempotencyKey);
        return new FinalizationResult(existing, Created: false);
    }
}
