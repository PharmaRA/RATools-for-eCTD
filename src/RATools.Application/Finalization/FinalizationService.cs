using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Finalization;

// Finalization records the identity of content that was actually validated. It re-reads
// the completed run, verifies that the run's binding names exactly the requested content
// and history, and archives the report's readiness verbatim. It never upgrades a
// development or incomplete report into a finalizable one and never re-runs validation
// against a different package.
public sealed class FinalizationService(
    IFinalizationStore store,
    IPackageValidationStore packageValidationStore,
    IWorkspaceRevisionStore revisions)
{
    // The tool version participates in the snapshot identity. Bump it whenever the
    // meaning of a captured manifest changes so old snapshots stay interpretable.
    public const string ToolVersion = "ratools-finalization-v1";

    public async Task<FinalizationResult> FinalizeAsync(FinalizeSequenceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await store.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return Replay(existing, request);

        var currentRevision = await revisions.GetRevisionAsync(request.ApplicationId, request.SequenceNumber.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (currentRevision is null)
            throw new FinalizationNotFoundException($"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}.");
        if (currentRevision != request.ExpectedRevision)
            throw new WorkspaceRevisionConflictException(request.ExpectedRevision, currentRevision.Value);

        var run = await packageValidationStore.GetRunAsync(request.PackageValidationRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinalizationNotAuthorizedException("The referenced package validation run does not exist.");
        if (run.ApplicationId != request.ApplicationId ||
            !string.Equals(run.SequenceNumber, request.SequenceNumber.Trim(), StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The referenced validation run belongs to a different application or sequence.");
        if (run.Status != ValidationRunStatus.Completed || run.ReportSha256 is null)
            throw new FinalizationNotAuthorizedException("Only a completed validation run can be finalized.");
        // The manifest digest the operator selected must be the exact package the report
        // validated. A matching application/sequence is not enough: a different package
        // for the same sequence is still a different input.
        if (!string.Equals(run.Binding.InputManifestDigest, request.ManifestDigest, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The selected manifest digest is not the exact package validated by this run.");
        if (!string.Equals(run.Binding.HistoryBaselineDigest, request.HistoryBaselineDigest, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The requested history baseline is not the baseline validated by this run.");
        if (!string.Equals(run.Binding.ProfileSnapshotId, request.StandardsProfileSnapshotId, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The requested standards snapshot is not the profile validated by this run.");

        var reportJson = await packageValidationStore.GetReportJsonAsync(run.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new FinalizationNotAuthorizedException("The validation run has no readable archived report.");
        // Only the report's own recorded outcome is used. The JSON is parsed rather than
        // reinterpreting the run: readiness and blocking reasons must be the archived
        // facts, never a fresh evaluation from today's rules.
        using var reportDocument = JsonDocument.Parse(reportJson);
        var reportRoot = reportDocument.RootElement;
        if (reportRoot.GetProperty("schemaVersion").GetInt32() != 1 ||
            !string.Equals(reportRoot.GetProperty("runStatus").GetString(), "Completed", StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException("The archived report is not a completed schema-1 report.");
        var reportReady = reportRoot.GetProperty("isReadyForFinalization").GetBoolean();
        var reportReasons = reportRoot.GetProperty("blockingReasons").EnumerateArray()
            .Select(reason => reason.GetString() ?? string.Empty).ToArray();
        // A formal snapshot is authorization to deliver; the service refuses an
        // unready formal report here instead of surfacing a domain argument error.
        if (run.Binding.ValidationMode == PackageValidationMode.Formal && !reportReady)
            throw new FinalizationNotAuthorizedException(
                "A formal finalization requires a report that is ready for finalization: " + string.Join(" | ", reportReasons));

        var sequence = FinalizedSequence.Create(request.ApplicationId, request.SequenceNumber.Trim(),
            request.ExpectedRevision, request.ManifestDigest, request.StandardsProfileSnapshotId, request.IchEctdVersion,
            request.RegionalModule1Version, request.HistoryBaselineDigest, run.Id, run.Binding.InputManifestDigest,
            run.ReportSha256, run.Binding.ValidationMode, reportReady, reportReasons, ToolVersion,
            request.IdempotencyKey, DateTime.UtcNow);
        var registered = await store.RegisterAsync(sequence, cancellationToken).ConfigureAwait(false);
        return new FinalizationResult(registered.Sequence, registered.Created);
    }

    public async Task<IReadOnlyList<FinalizedSequence>> ListAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default) =>
        await store.ListBySequenceAsync(applicationId, sequenceNumber.Trim(), cancellationToken).ConfigureAwait(false);

    public async Task<FinalizedSequence> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await store.GetAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new FinalizationNotFoundException($"Finalized sequence {id} was not found.");

    public async Task<DeliveryRecord> RegisterDeliveryAsync(Guid finalizedSequenceId, string packageSha256, string channel,
        string? externalReference, string? note, string recordedBy, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(finalizedSequenceId, cancellationToken).ConfigureAwait(false);
        if (!snapshot.IsReadyForFinalization || snapshot.ValidationMode != PackageValidationMode.Formal)
            throw new FinalizationNotAuthorizedException(
                "Delivery facts cannot be recorded against a development or non-ready snapshot.");
        var record = DeliveryRecord.Create(finalizedSequenceId, packageSha256, channel, externalReference, note, recordedBy,
            DateTime.UtcNow);
        await store.AppendDeliveryAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<DeliveryReceipt> RegisterReceiptAsync(Guid deliveryRecordId, DeliveryReceiptKind kind, string outcome,
        string rawText, string? externalReference, string recordedBy, CancellationToken cancellationToken = default)
    {
        if (deliveryRecordId == Guid.Empty) throw new ArgumentException("A delivery record is required.", nameof(deliveryRecordId));
        var receipt = DeliveryReceipt.Create(deliveryRecordId, kind, outcome, rawText, externalReference, recordedBy,
            DateTime.UtcNow);
        await store.AppendReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    // Replaying an idempotency key returns the committed snapshot only when every
    // request claim matches; a changed request must never silently reuse old content.
    private static FinalizationResult Replay(FinalizedSequence existing, FinalizeSequenceRequest request)
    {
        if (existing.ApplicationId != request.ApplicationId ||
            !string.Equals(existing.SequenceNumber, request.SequenceNumber.Trim(), StringComparison.Ordinal) ||
            existing.SourceWorkspaceRevision != request.ExpectedRevision ||
            !string.Equals(existing.ManifestDigest, request.ManifestDigest, StringComparison.Ordinal) ||
            !string.Equals(existing.StandardsProfileSnapshotId, request.StandardsProfileSnapshotId, StringComparison.Ordinal) ||
            !string.Equals(existing.IchEctdVersion, request.IchEctdVersion, StringComparison.Ordinal) ||
            !string.Equals(existing.RegionalModule1Version, string.IsNullOrWhiteSpace(request.RegionalModule1Version)
                ? null : request.RegionalModule1Version.Trim(), StringComparison.Ordinal) ||
            !string.Equals(existing.HistoryBaselineDigest, request.HistoryBaselineDigest, StringComparison.Ordinal) ||
            existing.ValidationRunId != request.PackageValidationRunId)
            throw new FinalizationIdempotencyConflictException(request.IdempotencyKey);
        return new FinalizationResult(existing, Created: false);
    }
}
