using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresPersistenceTransactionTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task ExecuteAsync_RollsBackDocumentAndPlacementAcrossSaveChanges()
    {
        var ids = await SeedAsync();

        await using var dbContext = fixture.CreateDbContext();
        var transaction = new EfCorePersistenceTransaction(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(async ct =>
        {
            var document = await dbContext.Documents.SingleAsync(x => x.Id == ids.DocumentId, ct);
            document.FileName = "renamed.pdf";
            document.StoragePath = document.StoragePath.Replace("protocol.pdf", "renamed.pdf", StringComparison.Ordinal);
            await dbContext.SaveChangesAsync(ct);

            var placement = await dbContext.DocumentPlacements.SingleAsync(x => x.Id == ids.PlacementId, ct);
            placement.Title = "Updated title";
            await dbContext.SaveChangesAsync(ct);

            throw new InvalidOperationException("simulated metadata update failure");
        }));

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal("protocol.pdf", (await verifyContext.Documents.SingleAsync(x => x.Id == ids.DocumentId)).FileName);
        Assert.Equal("Original title", (await verifyContext.DocumentPlacements.SingleAsync(x => x.Id == ids.PlacementId)).Title);
    }

    private async Task<(Guid DocumentId, Guid PlacementId)> SeedAsync()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var dbContext = fixture.CreateDbContext();
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
}
