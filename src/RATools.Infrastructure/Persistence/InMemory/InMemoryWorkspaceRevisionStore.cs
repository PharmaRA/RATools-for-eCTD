using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryWorkspaceRevisionStore(IApplicationRepository applications) : IWorkspaceRevisionStore
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(applicationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new ApplicationLock(gate);
    }

    public async Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) =>
        (await applications.GetAsync(applicationId, cancellationToken))?.Sequences.SingleOrDefault(sequence => sequence.SequenceNumber == sequenceNumber)?.WorkspaceRevision;

    public async Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken);
        var sequence = application?.Sequences.SingleOrDefault(sequence => sequence.SequenceNumber == sequenceNumber);
        if (sequence is null || sequence.WorkspaceRevision != expectedRevision) return false;
        sequence.AdvanceWorkspaceRevision(expectedRevision);
        await applications.UpdateAsync(application!, cancellationToken);
        return true;
    }

    private sealed class ApplicationLock(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; }
    }
}
