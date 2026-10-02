using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Abstractions.Persistence;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreWorkspaceRevisionStore(RAToolsDbContext dbContext) : IWorkspaceRevisionStore
{
    public async Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var key = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(applicationId.ToByteArray()));
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new ApplicationLock(dbContext, key);
        }
        catch { await dbContext.Database.CloseConnectionAsync(); throw; }
    }

    public Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) =>
        dbContext.Sequences.AsNoTracking().Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .Select(row => (long?)row.WorkspaceRevision).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null) throw new InvalidOperationException("Revision writes require a database transaction.");
        var changed = await dbContext.Sequences.Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber &&
            row.WorkspaceRevision == expectedRevision).ExecuteUpdateAsync(update => update.SetProperty(row => row.WorkspaceRevision, expectedRevision + 1), cancellationToken);
        if (changed == 0) return false;
        foreach (var entry in dbContext.ChangeTracker.Entries<SequenceRecord>().Where(entry =>
                     entry.Entity.ApplicationId == applicationId && entry.Entity.SequenceNumber == sequenceNumber).ToArray())
        {
            var revision = entry.Property(row => row.WorkspaceRevision);
            revision.CurrentValue = expectedRevision + 1;
            revision.OriginalValue = expectedRevision + 1;
            revision.IsModified = false;
        }
        return true;
    }

    private sealed class ApplicationLock(RAToolsDbContext context, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteNonQueryAsync(timeout.Token);
            }
            catch { NpgsqlConnection.ClearPool(connection); }
            finally { await context.Database.CloseConnectionAsync(); }
        }
    }
}
