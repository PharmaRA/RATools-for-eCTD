using RATools.Application.Abstractions.Persistence;

namespace RATools.Application.Persistence;

public sealed class PassthroughPersistenceTransaction : IPersistenceTransaction
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}
