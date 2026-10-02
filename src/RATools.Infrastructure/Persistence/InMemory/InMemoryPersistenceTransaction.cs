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
            foreach (var commit in pending.Commits) commit();
            return result;
        }
        finally { Current.Value = null; }
    }

    internal sealed class PendingWrites
    {
        public Dictionary<object, object> Changes { get; } = new();
        public List<Action> Commits { get; } = [];
    }
}
