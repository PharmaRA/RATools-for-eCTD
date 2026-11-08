using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Persistence;

public sealed class SqliteTestConstraintTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("id-length", false)]
    [InlineData("id-character", false)]
    [InlineData("hash-character", false)]
    public async Task SnapshotHashConstraintsRemainEnforced(string variant, bool accepted)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseSqlite(connection).ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>().Options);
        await database.Database.EnsureCreatedAsync();
        database.Set<StandardsSnapshotRecord>().Add(new()
        {
            Id = "sha256:" + (variant == "id-length" ? new string('a', 63) : variant == "id-character" ? new string('g', 64) : new string('a', 64)),
            SchemaVersion = 1, TemplateKey = "test", RuleProfileAlias = "test", Data = "{}",
            DataSha256 = variant == "hash-character" ? new string('G', 64) : new string('a', 64), AcquiredUtc = DateTime.UtcNow
        });
        if (accepted) Assert.Equal(1, await database.SaveChangesAsync());
        else Assert.Equal(19, Assert.IsType<SqliteException>((await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync())).InnerException).SqliteErrorCode);
    }
}
