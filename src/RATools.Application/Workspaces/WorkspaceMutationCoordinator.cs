using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;

namespace RATools.Application.Workspaces;

public sealed class WorkspaceRevisionRequiredException() : Exception("Workspace revision is required. Refresh the client and retry.");
public sealed class WorkspaceRevisionTargetNotFoundException() : Exception("The target workspace no longer exists.");
public sealed class WorkspaceRevisionInvalidException() : Exception("Workspace revision must be a nonnegative safe integer below the revision limit.");

public sealed class WorkspaceMutationCoordinator(IWorkspaceRevisionStore revisions, IPersistenceTransaction transactions,
    RATools.Application.Ctd.NodeFileMoveGuard? moveGuard = null, FinalizedWorkspaceGuard? finalizedGuard = null)
{
    // Every draft write funnels through here, so this is the single place that proves the
    // sequence is still editable. The check runs under the application lock: a concurrent
    // finalization either completes first (and this write is refused) or cannot start
    // until this revision has advanced.
    public Task<WorkspaceMutation> AcquireAsync(Guid applicationId, string sequenceNumber, long? expectedRevision,
        CancellationToken cancellationToken = default) =>
        AcquireCoreAsync(applicationId, sequenceNumber, expectedRevision, requireEditable: true, cancellationToken);

    // Only opening a finalized draft may write to a sequence that the guard would refuse;
    // it is the explicit continuation action, not an ordinary edit.
    public Task<WorkspaceMutation> AcquireContinuationAsync(Guid applicationId, string sequenceNumber,
        long? expectedRevision, CancellationToken cancellationToken = default) =>
        AcquireCoreAsync(applicationId, sequenceNumber, expectedRevision, requireEditable: false, cancellationToken);

    private async Task<WorkspaceMutation> AcquireCoreAsync(Guid applicationId, string sequenceNumber,
        long? expectedRevision, bool requireEditable, CancellationToken cancellationToken)
    {
        if (expectedRevision is null) throw new WorkspaceRevisionRequiredException();
        if (expectedRevision.Value is < 0 or >= 9007199254740991L) throw new WorkspaceRevisionInvalidException();
        var heldLock = await revisions.LockApplicationAsync(applicationId, cancellationToken);
        try
        {
            if (moveGuard is not null) await moveGuard.EnsureReadyAsync(applicationId, cancellationToken);
            if (requireEditable && finalizedGuard is not null)
                await finalizedGuard.EnsureEditableAsync(applicationId, sequenceNumber, cancellationToken);
            var current = await revisions.GetRevisionAsync(applicationId, sequenceNumber, cancellationToken)
                ?? throw new WorkspaceRevisionTargetNotFoundException();
            if (current != expectedRevision) throw new WorkspaceRevisionConflictException(expectedRevision.Value, current);
            return new WorkspaceMutation(revisions, transactions, heldLock, applicationId, sequenceNumber, current);
        }
        catch { await heldLock.DisposeAsync(); throw; }
    }
}

public sealed class WorkspaceMutation(IWorkspaceRevisionStore revisions, IPersistenceTransaction transactions,
    IAsyncDisposable heldLock, Guid applicationId, string sequenceNumber, long expectedRevision) : IAsyncDisposable
{
    private readonly long _expectedRevision = expectedRevision;
    public long Revision { get; private set; } = expectedRevision;

    public async Task<T> CommitAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var result = await transactions.ExecuteAsync(async ct =>
        {
            var outcome = await operation(ct);
            if (!await revisions.AdvanceAsync(applicationId, sequenceNumber, _expectedRevision, ct))
                throw new WorkspaceRevisionConflictException(_expectedRevision, await revisions.GetRevisionAsync(applicationId, sequenceNumber, ct));
            return outcome;
        }, cancellationToken);
        Revision = _expectedRevision + 1;
        return result;
    }

    public Task CommitAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        CommitAsync(async ct => { await operation(ct); return true; }, cancellationToken);

    public async Task ReconcileAsync(bool preservesChangedState, Func<CancellationToken, Task> compensation, CancellationToken cancellationToken)
    {
        var current = await revisions.GetRevisionAsync(applicationId, sequenceNumber, cancellationToken)
            ?? throw new WorkspaceRevisionTargetNotFoundException();
        if (current != _expectedRevision && current != _expectedRevision + 1)
            throw new WorkspaceRevisionConflictException(_expectedRevision, current);
        await transactions.ExecuteAsync(async ct =>
        {
            if (preservesChangedState && current == _expectedRevision)
            {
                if (!await revisions.AdvanceAsync(applicationId, sequenceNumber, current, ct))
                    throw new WorkspaceRevisionConflictException(current, await revisions.GetRevisionAsync(applicationId, sequenceNumber, ct));
                current++;
            }
            await compensation(ct);
        }, cancellationToken);
        Revision = current;
    }

    public ValueTask DisposeAsync() => heldLock.DisposeAsync();
}
