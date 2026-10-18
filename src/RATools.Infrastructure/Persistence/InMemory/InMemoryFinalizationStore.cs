using RATools.Application.Finalization;
using RATools.Domain.Finalization;

namespace RATools.Infrastructure.Persistence.InMemory;

// Development/test provider. It follows the durable contract: immutable snapshots,
// idempotent registration, one snapshot per application/sequence/manifest, and
// append-only delivery facts. It is not durable storage.
public sealed class InMemoryFinalizationStore : IFinalizationStore
{
    private readonly Dictionary<Guid, FinalizedSequence> _sequences = [];
    private readonly Dictionary<string, Guid> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid ApplicationId, string SequenceNumber, string Digest), Guid> _byContent = [];
    private readonly List<DeliveryRecord> _deliveries = [];
    private readonly List<DeliveryReceipt> _receipts = [];
    private readonly object _gate = new();

    public Task<FinalizationRegisterResult> RegisterAsync(FinalizedSequence sequence, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_byKey.TryGetValue(sequence.IdempotencyKey, out var existingId))
            {
                var existing = _sequences[existingId];
                if (existing.RequestDigest != sequence.RequestDigest)
                    throw new FinalizationIdempotencyConflictException(sequence.IdempotencyKey);
                return Task.FromResult(new FinalizationRegisterResult(existing, false));
            }
            var contentKey = (sequence.ApplicationId, sequence.SequenceNumber, sequence.ManifestDigest);
            if (_byContent.TryGetValue(contentKey, out var contentId))
                throw new FinalizationSnapshotExistsException(contentId);
            _sequences[sequence.Id] = sequence;
            _byKey[sequence.IdempotencyKey] = sequence.Id;
            _byContent[contentKey] = sequence.Id;
            return Task.FromResult(new FinalizationRegisterResult(sequence, true));
        }
    }

    public Task<FinalizedSequence?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_sequences.GetValueOrDefault(id));
    }

    public Task<FinalizedSequence?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_byKey.TryGetValue(idempotencyKey, out var id) ? _sequences.GetValueOrDefault(id) : null);
    }

    public Task<IReadOnlyList<FinalizedSequence>> ListBySequenceAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<FinalizedSequence>>(_sequences.Values
                .Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
                .OrderBy(row => row.CreatedUtc).ToArray());
    }

    public Task<IReadOnlyList<DeliveryRecord>> ListDeliveriesAsync(Guid finalizedSequenceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<DeliveryRecord>>(_deliveries
                .Where(row => row.FinalizedSequenceId == finalizedSequenceId).OrderBy(row => row.RecordedUtc).ToArray());
    }

    public Task AppendDeliveryAsync(DeliveryRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sequences.ContainsKey(record.FinalizedSequenceId))
                throw new FinalizationNotFoundException($"Finalized sequence {record.FinalizedSequenceId} was not found.");
            _deliveries.Add(record);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<DeliveryReceipt>> ListReceiptsAsync(Guid deliveryRecordId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<DeliveryReceipt>>(_receipts
                .Where(row => row.DeliveryRecordId == deliveryRecordId).OrderBy(row => row.RecordedUtc).ToArray());
    }

    public Task AppendReceiptAsync(DeliveryReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_deliveries.Any(row => row.Id == receipt.DeliveryRecordId))
                throw new FinalizationNotFoundException($"Delivery record {receipt.DeliveryRecordId} was not found.");
            _receipts.Add(receipt);
            return Task.CompletedTask;
        }
    }
}
