using RATools.Application.Abstractions.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Finalization;

namespace RATools.Application.Finalization;

// Explicitly continues a finalized sequence. This is the only write allowed while the
// latest snapshot has no draft: it pins which delivered version the new edits belong to
// and advances the workspace revision in the same transaction as the draft record.
public sealed class FinalizationDraftService(IFinalizationStore store, WorkspaceMutationCoordinator mutations)
{
    public async Task<FinalizationDraftResult> OpenAsync(FinalizationDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FinalizedSequenceId == Guid.Empty)
            throw new FinalizationNotFoundException("A finalized sequence is required.");

        // An exact replay returns the committed draft before any state check: the request
        // has already succeeded, and later edits or finalizations must not turn it into
        // an error.
        var replay = await store.FindDraftByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.FinalizedSequenceId != request.FinalizedSequenceId)
                throw new FinalizationIdempotencyConflictException(request.IdempotencyKey);
            return new FinalizationDraftResult(replay, Created: false);
        }

        var snapshot = await store.GetAsync(request.FinalizedSequenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinalizationNotFoundException($"Finalized sequence {request.FinalizedSequenceId} was not found.");
        // Only the newest snapshot may be continued; a draft from a superseded snapshot
        // would make the editable content ambiguous.
        var latest = await store.LatestBySequenceAsync(snapshot.ApplicationId, snapshot.SequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (latest is not null && latest.Id != snapshot.Id)
            throw new FinalizationDraftRequiresLatestException(snapshot.Id, latest.Id);
        var existing = await store.FindDraftAsync(snapshot.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new FinalizationDraftAlreadyOpenException(snapshot.Id, existing.WorkspaceRevision);

        await using var mutation = await mutations.AcquireContinuationAsync(snapshot.ApplicationId,
            snapshot.SequenceNumber, request.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        // Re-check under the application lock: another request may have opened the draft,
        // or a finalization may have committed a newer snapshot, between the reads above
        // and acquiring the lock. A draft from the superseded snapshot would advance the
        // revision without making anything editable.
        latest = await store.LatestBySequenceAsync(snapshot.ApplicationId, snapshot.SequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (latest is not null && latest.Id != snapshot.Id)
            throw new FinalizationDraftRequiresLatestException(snapshot.Id, latest.Id);
        existing = await store.FindDraftAsync(snapshot.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new FinalizationDraftAlreadyOpenException(snapshot.Id, existing.WorkspaceRevision);
        var draft = FinalizedDraftRevision.Create(snapshot.Id, snapshot.ApplicationId, snapshot.SequenceNumber,
            snapshot.SourceWorkspaceRevision, mutation.Revision + 1, request.IdempotencyKey, DateTime.UtcNow);
        var committed = await mutation.CommitAsync(ct => store.AppendDraftAsync(draft, ct), cancellationToken)
            .ConfigureAwait(false);
        // A concurrent request may have committed first; the store returns that row instead
        // of creating a second continuation. A different snapshot under this key is an
        // idempotency conflict, not a second draft of this snapshot.
        if (committed.FinalizedSequenceId != snapshot.Id)
            throw new FinalizationIdempotencyConflictException(request.IdempotencyKey);
        if (committed.Id != draft.Id)
            throw new FinalizationDraftAlreadyOpenException(snapshot.Id, committed.WorkspaceRevision);
        return new FinalizationDraftResult(committed, Created: true);
    }

    public Task<FinalizedDraftRevision?> FindAsync(Guid finalizedSequenceId, CancellationToken cancellationToken = default) =>
        store.FindDraftAsync(finalizedSequenceId, cancellationToken);
}
