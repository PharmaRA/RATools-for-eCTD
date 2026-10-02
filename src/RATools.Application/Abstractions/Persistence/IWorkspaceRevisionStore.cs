namespace RATools.Application.Abstractions.Persistence;

public interface IWorkspaceRevisionStore
{
    Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default);
    Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision, CancellationToken cancellationToken = default);
}
