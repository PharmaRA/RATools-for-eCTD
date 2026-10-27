using RATools.Domain.Finalization;

namespace RATools.Application.Finalization;

// The finalized/editable state of one sequence as seen by the workspace snapshot and the
// write boundary. Null means no snapshot exists: the sequence is an ordinary draft.
public sealed record WorkspaceFinalizationState(Guid FinalizedSequenceId, long SourceWorkspaceRevision,
    bool IsEditable, long? DraftWorkspaceRevision);

// P4-07 write boundary. Every delivery-affecting workspace write acquires the
// application lock first; this guard then proves the sequence is still editable. A
// finalized sequence stays read-only until an operator opens an explicit draft from the
// latest snapshot, so a legacy endpoint cannot modify delivered content by accident.
public sealed class FinalizedWorkspaceGuard(IFinalizationStore store)
{
    public async Task EnsureEditableAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        if (await GetStateAsync(applicationId, sequenceNumber, cancellationToken).ConfigureAwait(false) is not
            { IsEditable: false } state) return;
        throw new WorkspaceFinalizedException(applicationId, sequenceNumber, state.FinalizedSequenceId,
            state.SourceWorkspaceRevision);
    }

    public async Task<WorkspaceFinalizationState?> GetStateAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var latest = await store.LatestBySequenceAsync(applicationId, sequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (latest is null) return null;
        var draft = await store.FindDraftAsync(latest.Id, cancellationToken).ConfigureAwait(false);
        return new WorkspaceFinalizationState(latest.Id, latest.SourceWorkspaceRevision, draft is not null,
            draft?.WorkspaceRevision);
    }
}
