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
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, "IX_finalized_sequences_IdempotencyKey"))
        {
            // A concurrent registration with the same key won. Re-read and decide whether
            // this is the same committed request or a conflict.
            dbContext.ChangeTracker.Clear();
            var winner = await FindByIdempotencyKeyAsync(sequence.IdempotencyKey, cancellationToken).ConfigureAwait(false)
                ?? throw new FinalizationIdempotencyConflictException(sequence.IdempotencyKey);
            return Replay(winner, sequence);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, "IX_finalized_sequences_content"))
        {
            dbContext.ChangeTracker.Clear();
            var content = await dbContext.Set<FinalizedSequenceRecord>().AsNoTracking()
                .Where(row => row.ApplicationId == sequence.ApplicationId && row.SequenceNumber == sequence.SequenceNumber &&
                    row.ManifestDigest == sequence.ManifestDigest)
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

    private static bool IsUniqueViolation(DbUpdateException exception, string constraint) =>
        exception.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(postgres.ConstraintName, constraint, StringComparison.Ordinal);
}
