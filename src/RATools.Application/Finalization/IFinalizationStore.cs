using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Finalization;

public sealed record FinalizationRegisterResult(FinalizedSequence Sequence, bool Created);

// Finalized snapshots are immutable; delivery records and receipts are append-only.
// The store enforces idempotency at the persistence boundary: the same key with the same
// committed identity digest returns the existing snapshot, a different one conflicts.
public interface IFinalizationStore
{
    Task<FinalizationRegisterResult> RegisterAsync(FinalizedSequence sequence, CancellationToken cancellationToken = default);
    Task<FinalizedSequence?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FinalizedSequence?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinalizedSequence>> ListBySequenceAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default);
    // The newest snapshot for a sequence: the boundary that decides whether editing is
    // still allowed and which snapshot a new draft continues.
    Task<FinalizedSequence?> LatestBySequenceAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default);
    // Retention guard: a sequence (or application) that owns finalized evidence cannot be
    // deleted through the ordinary draft-deletion path.
    Task<bool> AnyByApplicationAsync(Guid applicationId, string? sequenceNumber = null,
        CancellationToken cancellationToken = default);

    // A snapshot can continue in at most one draft; the store enforces this for
    // concurrent requests, and the finder drives both the write boundary and replay.
    Task<FinalizedDraftRevision?> FindDraftAsync(Guid finalizedSequenceId,
        CancellationToken cancellationToken = default);
    Task<FinalizedDraftRevision?> FindDraftByIdempotencyKeyAsync(string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<FinalizedDraftRevision> AppendDraftAsync(FinalizedDraftRevision draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeliveryRecord>> ListDeliveriesAsync(Guid finalizedSequenceId, CancellationToken cancellationToken = default);
    Task AppendDeliveryAsync(DeliveryRecord record, CancellationToken cancellationToken = default);
    Task<bool> DeliveryExistsAsync(Guid deliveryRecordId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryReceipt>> ListReceiptsAsync(Guid deliveryRecordId, CancellationToken cancellationToken = default);
    Task AppendReceiptAsync(DeliveryReceipt receipt, CancellationToken cancellationToken = default);
}

// A snapshot request names a completed validation run whose archived report is the
// readiness evidence for the exact captured content. Development runs may be archived as
// incomplete snapshots; only a formal, ready report authorizes delivery records.
public sealed record FinalizeSequenceRequest(Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    Guid PackageValidationRunId, string ManifestDigest, string StandardsProfileSnapshotId, string IchEctdVersion,
    string? RegionalModule1Version, string HistoryBaselineDigest, string IdempotencyKey);

public sealed record FinalizationResult(FinalizedSequence Sequence, bool Created);

public sealed class FinalizationIdempotencyConflictException(string idempotencyKey)
    : InvalidOperationException($"Idempotency key '{idempotencyKey}' already identifies a different finalization request.");

public sealed class FinalizationNotAuthorizedException(string message) : InvalidOperationException(message);

public sealed class FinalizationSnapshotExistsException(Guid finalizedSequenceId)
    : InvalidOperationException($"Sequence content is already finalized as snapshot {finalizedSequenceId}; create a new draft revision to change it.");

public sealed class FinalizationNotFoundException(string message) : KeyNotFoundException(message);

public sealed record FinalizationDraftRequest(Guid FinalizedSequenceId, long ExpectedRevision, string IdempotencyKey);

public sealed record FinalizationDraftResult(FinalizedDraftRevision Draft, bool Created);
