using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

public sealed class EfCorePersistenceTransactionTests
{
    [Fact]
    public async Task ExecuteAsync_CommitsDocumentAndPlacementSaveChangesTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var ids = await SeedAsync(dbContext);
        var transaction = new EfCorePersistenceTransaction(dbContext);

        var updateCount = await transaction.ExecuteAsync(async ct =>
        {
            var document = await dbContext.Documents.SingleAsync(x => x.Id == ids.DocumentId, ct);
            document.FileName = "renamed.pdf";
            await dbContext.SaveChangesAsync(ct);

            var placement = await dbContext.DocumentPlacements.SingleAsync(x => x.Id == ids.PlacementId, ct);
            placement.Title = "Updated title";
            await dbContext.SaveChangesAsync(ct);
            return 2;
        });

        dbContext.ChangeTracker.Clear();
        Assert.Equal(2, updateCount);
        Assert.Equal("renamed.pdf", (await dbContext.Documents.SingleAsync(x => x.Id == ids.DocumentId)).FileName);
        Assert.Equal("Updated title", (await dbContext.DocumentPlacements.SingleAsync(x => x.Id == ids.PlacementId)).Title);
    }

    [Fact]
    public async Task ExecuteAsync_RollsBackDocumentAndPlacementAfterMultipleSaveChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var ids = await SeedAsync(dbContext);
        var transaction = new EfCorePersistenceTransaction(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(async ct =>
        {
            var document = await dbContext.Documents.SingleAsync(x => x.Id == ids.DocumentId, ct);
            document.FileName = "renamed.pdf";
            await dbContext.SaveChangesAsync(ct);

            var placement = await dbContext.DocumentPlacements.SingleAsync(x => x.Id == ids.PlacementId, ct);
            placement.Title = "Updated title";
            await dbContext.SaveChangesAsync(ct);

            throw new InvalidOperationException("simulated second update failure");
        }));

        dbContext.ChangeTracker.Clear();
        Assert.Equal("protocol.pdf", (await dbContext.Documents.SingleAsync(x => x.Id == ids.DocumentId)).FileName);
        Assert.Equal("Original title", (await dbContext.DocumentPlacements.SingleAsync(x => x.Id == ids.PlacementId)).Title);
    }

    private static async Task<(Guid DocumentId, Guid PlacementId)> SeedAsync(RAToolsDbContext dbContext)
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        dbContext.Applications.Add(new ApplicationRecord
        {
            Id = applicationId,
            ApplicationNumber = $"APP-{Guid.NewGuid():N}",
            Region = "US",
            SponsorName = "Sponsor",
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            WorkingDirectoryPath = $"C:/workspace/{applicationId:N}",
            CreatedUtc = now
        });
        dbContext.Sequences.Add(new SequenceRecord
        {
            ApplicationId = applicationId,
            SequenceNumber = "0001",
            SubmissionType = "original",
            Description = "Original sequence",
            CreatedUtc = now
        });
        dbContext.Documents.Add(new DocumentRecord
        {
            Id = documentId,
            FileName = "protocol.pdf",
            MediaType = "application/pdf",
            FileSize = 7,
            Sha256 = "sha256",
            Md5 = "md5",
            StoragePath = $"C:/workspace/{applicationId:N}/0001/protocol.pdf",
            CreatedUtc = now
        });
        dbContext.DocumentPlacements.Add(new DocumentPlacementRecord
        {
            Id = placementId,
            DocumentId = documentId,
            ApplicationId = applicationId,
            SequenceNumber = "0001",
            CtdSection = "m1.1",
            Operation = "New",
            Title = "Original title",
            CreatedUtc = now
        });
        await dbContext.SaveChangesAsync();
        return (documentId, placementId);
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
