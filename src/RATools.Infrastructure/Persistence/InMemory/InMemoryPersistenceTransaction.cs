using RATools.Application.Abstractions.Persistence;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryPersistenceTransaction : IPersistenceTransaction
{
    internal static readonly AsyncLocal<PendingWrites?> Current = new();

    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(async ct => { await operation(ct); return true; }, cancellationToken);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (Current.Value is not null) throw new InvalidOperationException("Nested memory transactions are not supported.");
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new PendingWrites();
        Current.Value = pending;
        try
        {
            var result = await operation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Application locks prevent workspace readers from observing a partial
            // commit. Only touched keys are applied, preserving other applications.
            Action commit = () => { foreach (var apply in pending.Commits) apply(); };
            foreach (var fence in pending.CommitFences.AsEnumerable().Reverse())
            {
                var next = commit;
                commit = () => fence(next);
            }
            commit();
            return result;
        }
        finally { Current.Value = null; }
    }

    internal sealed class PendingWrites
    {
        public Dictionary<object, object> Changes { get; } = new();
        public List<Action> Commits { get; } = [];
        public List<Action<Action>> CommitFences { get; } = [];
    }

    internal static void RegisterCommitFence(Action<Action> fence)
    {
        ArgumentNullException.ThrowIfNull(fence);
        (Current.Value ?? throw new InvalidOperationException("A commit fence requires an active in-memory transaction."))
            .CommitFences.Add(fence);
    }
}
