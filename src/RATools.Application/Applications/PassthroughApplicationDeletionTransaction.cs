namespace RATools.Application.Applications;

public sealed class PassthroughApplicationDeletionTransaction : IApplicationDeletionTransaction
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}
