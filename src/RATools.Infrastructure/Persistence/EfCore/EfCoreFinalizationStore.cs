using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Finalization;
using RATools.Domain.Finalization;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreFinalizationStore(RAToolsDbContext dbContext) : IFinalizationStore
{
    public async Task<FinalizationRegisterResult> RegisterAsync(FinalizedSequence sequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var existing = await FindByIdempotencyKeyAsync(sequence.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return Replay(existing, sequence);
        try
        {
            await dbContext.Set<FinalizedSequenceRecord>().AddAsync(sequence.ToRecord(), cancellationToken)
                .ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(sequence, Created: true);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent registration won. PostgreSQL reports only one violated index,
            // and a same-key/same-content retry violates both; always re-read the key
            // first so an identical retry is replayed rather than rejected.
            dbContext.ChangeTracker.Clear();
            var winner = await FindByIdempotencyKeyAsync(sequence.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (winner is not null) return Replay(winner, sequence);
            var content = await dbContext.Set<FinalizedSequenceRecord>().AsNoTracking()
                .Where(row => row.ApplicationId == sequence.ApplicationId && row.SequenceNumber == sequence.SequenceNumber &&
                    row.ManifestDigest == sequence.ManifestDigest && row.ValidationMode == sequence.ValidationMode.ToString())
                .Select(row => row.Id).SingleAsync(cancellationToken).ConfigureAwait(false);
            throw new FinalizationSnapshotExistsException(content);
        }
    }

    public async Task<FinalizedSequence?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Set<FinalizedSequenceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task<FinalizedSequence?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Set<FinalizedSequenceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken).ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<FinalizedSequence>> ListBySequenceAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Set<FinalizedSequenceRecord>().AsNoTracking()
            .Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .OrderBy(row => row.CreatedUtc).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyList<DeliveryRecord>> ListDeliveriesAsync(Guid finalizedSequenceId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Set<DeliveryRecordRow>().AsNoTracking()
            .Where(row => row.FinalizedSequenceId == finalizedSequenceId)
            .OrderBy(row => row.RecordedUtc).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.ToDomain()).ToArray();
    }

    public async Task AppendDeliveryAsync(DeliveryRecord record, CancellationToken cancellationToken = default)
    {
        var snapshotExists = await dbContext.Set<FinalizedSequenceRecord>().AsNoTracking()
            .AnyAsync(row => row.Id == record.FinalizedSequenceId, cancellationToken).ConfigureAwait(false);
        if (!snapshotExists)
            throw new FinalizationNotFoundException($"Finalized sequence {record.FinalizedSequenceId} was not found.");
        await AppendAsync(record.ToRecord(), cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeliveryExistsAsync(Guid deliveryRecordId, CancellationToken cancellationToken = default) =>
        dbContext.Set<DeliveryRecordRow>().AsNoTracking().AnyAsync(row => row.Id == deliveryRecordId, cancellationToken);

    public async Task<IReadOnlyList<DeliveryReceipt>> ListReceiptsAsync(Guid deliveryRecordId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Set<DeliveryReceiptRow>().AsNoTracking()
            .Where(row => row.DeliveryRecordId == deliveryRecordId)
            .OrderBy(row => row.RecordedUtc).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.ToDomain()).ToArray();
    }

    public async Task AppendReceiptAsync(DeliveryReceipt receipt, CancellationToken cancellationToken = default)
    {
        var deliveryExists = await dbContext.Set<DeliveryRecordRow>().AsNoTracking()
            .AnyAsync(row => row.Id == receipt.DeliveryRecordId, cancellationToken).ConfigureAwait(false);
        if (!deliveryExists)
            throw new FinalizationNotFoundException($"Delivery record {receipt.DeliveryRecordId} was not found.");
        await AppendAsync(receipt.ToRecord(), cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendAsync<T>(T record, CancellationToken cancellationToken) where T : class
    {
        try
        {
            await dbContext.Set<T>().AddAsync(record, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The append did not commit; detach the rejected entity so later reads in the
            // same scope do not observe a phantom row.
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static FinalizationRegisterResult Replay(FinalizedSequence existing, FinalizedSequence requested)
    {
        if (existing.SourceWorkspaceRevision != requested.SourceWorkspaceRevision ||
            !string.Equals(existing.ManifestDigest, requested.ManifestDigest, StringComparison.Ordinal) ||
            !string.Equals(existing.StandardsProfileSnapshotId, requested.StandardsProfileSnapshotId, StringComparison.Ordinal) ||
            !string.Equals(existing.IchEctdVersion, requested.IchEctdVersion, StringComparison.Ordinal) ||
            !string.Equals(existing.RegionalModule1Version, requested.RegionalModule1Version, StringComparison.Ordinal) ||
            !string.Equals(existing.HistoryBaselineDigest, requested.HistoryBaselineDigest, StringComparison.Ordinal) ||
            existing.ValidationRunId != requested.ValidationRunId ||
            !string.Equals(existing.RequestDigest, requested.RequestDigest, StringComparison.Ordinal))
            throw new FinalizationIdempotencyConflictException(requested.IdempotencyKey);
        return new(existing, Created: false);
    }

    // Any unique violation on this table means a concurrent registration won one of the
    // two unique constraints; the caller re-reads to decide which outcome applies.
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        // The content index is filtered to this table's schema; its reported constraint
        // name is stable. The idempotency index is the only other unique constraint.
        postgres.ConstraintName is "IX_finalized_sequences_content" or "IX_finalized_sequences_IdempotencyKey";
}
