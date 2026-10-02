using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Persistence;
using RATools.Application.Workspaces;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.TestDoubles;

internal static class TestWorkspaceMutations
{
    public static WorkspaceMutationCoordinator Create(IApplicationRepository applications,
        IPersistenceTransaction? transactions = null, RAToolsDbContext? database = null)
    {
        var locks = new InMemoryWorkspaceRevisionStore(applications);
        IWorkspaceRevisionStore store = database is null ? locks : new RelationalStore(database, locks);
        return new WorkspaceMutationCoordinator(store, transactions ?? new PassthroughPersistenceTransaction());
    }

    // Existing SQLite file-compensation regressions use real transactional revision
    // updates; PostgreSQL tests separately exercise production advisory locking.
    private sealed class RelationalStore(RAToolsDbContext database, IWorkspaceRevisionStore locks) : IWorkspaceRevisionStore
    {
        public Task<IAsyncDisposable> LockApplicationAsync(Guid id, CancellationToken cancellationToken = default) =>
            locks.LockApplicationAsync(id, cancellationToken);

        public Task<long?> GetRevisionAsync(Guid id, string sequence, CancellationToken cancellationToken = default) =>
            database.Sequences.AsNoTracking().Where(row => row.ApplicationId == id && row.SequenceNumber == sequence)
                .Select(row => (long?)row.WorkspaceRevision).SingleOrDefaultAsync(cancellationToken);

        public Task<bool> AdvanceAsync(Guid id, string sequence, long expectedRevision, CancellationToken cancellationToken = default) =>
            new EfCoreWorkspaceRevisionStore(database).AdvanceAsync(id, sequence, expectedRevision, cancellationToken);
    }
}
