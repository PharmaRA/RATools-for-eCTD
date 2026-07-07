using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

public sealed class EfCoreApplicationDeletionTransactionTests
{
    [Fact]
    public async Task ExecuteAsync_CommitsAllWritesWhenOperationSucceeds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var documentId = Guid.NewGuid();
        dbContext.Documents.Add(CreateDocumentRecord(documentId));
        await dbContext.SaveChangesAsync();

        var transaction = new EfCoreApplicationDeletionTransaction(dbContext);

        await transaction.ExecuteAsync(async ct =>
        {
            var record = await dbContext.Documents.SingleAsync(x => x.Id == documentId, ct);
            dbContext.Documents.Remove(record);
            await dbContext.SaveChangesAsync(ct);
        });

        Assert.False(await dbContext.Documents.AnyAsync(x => x.Id == documentId));
    }

    [Fact]
    public async Task ExecuteAsync_RollsBackPartialDeletesWhenOperationThrows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        dbContext.Documents.Add(CreateDocumentRecord(firstId));
        dbContext.Documents.Add(CreateDocumentRecord(secondId));
        await dbContext.SaveChangesAsync();

        var transaction = new EfCoreApplicationDeletionTransaction(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(async ct =>
        {
            var first = await dbContext.Documents.SingleAsync(x => x.Id == firstId, ct);
            dbContext.Documents.Remove(first);
            await dbContext.SaveChangesAsync(ct);

            // 模拟删除编排中途失败，事务应整体回滚。
            throw new InvalidOperationException("simulated mid-delete failure");
        }));

        // 回滚后两行都应保留，没有部分删除残留。
        dbContext.ChangeTracker.Clear();
        Assert.True(await dbContext.Documents.AnyAsync(x => x.Id == firstId));
        Assert.True(await dbContext.Documents.AnyAsync(x => x.Id == secondId));
    }

    private static DocumentRecord CreateDocumentRecord(Guid id)
    {
        return new DocumentRecord
        {
            Id = id,
            FileName = "doc.pdf",
            MediaType = "application/pdf",
            FileSize = 10,
            Sha256 = "sha",
            Md5 = "md5",
            StoragePath = $"C:/workspace/{id:N}/doc.pdf",
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static async Task<RAToolsDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new RAToolsDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();

        return dbContext;
    }
}
