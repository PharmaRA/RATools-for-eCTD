using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// <see cref="EfCoreApplicationDeletionTransaction"/> 在真实 PostgreSQL 上的回滚行为。
/// SQLite 版本（<c>EfCoreApplicationDeletionTransactionTests</c>）已覆盖"编排抛异常则回滚"，
/// 这里补的是只有真 PG 才成立的语义：**事务内任一语句失败会使整个事务进入 aborted 状态**，
/// 后续写入无法提交——删除编排必须整体回滚，不能留下部分删除的残局。
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresDeletionTransactionTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task ExecuteAsync_RollsBackEarlierDeletesWhenOrchestrationThrows()
    {
        var applicationId = Guid.NewGuid();
        await SeedAsync(applicationId, "0000", "0001");

        await using var dbContext = fixture.CreateDbContext();
        var transaction = new EfCoreApplicationDeletionTransaction(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(async ct =>
        {
            var sequence = await dbContext.Sequences.SingleAsync(
                x => x.ApplicationId == applicationId && x.SequenceNumber == "0000", ct);
            dbContext.Sequences.Remove(sequence);
            await dbContext.SaveChangesAsync(ct);

            throw new InvalidOperationException("simulated mid-delete failure");
        }));

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(2, await verifyContext.Sequences.CountAsync(x => x.ApplicationId == applicationId));
    }

    [RequiresPostgresFact]
    public async Task ExecuteAsync_RollsBackWhenAConstraintViolationAbortsTheTransaction()
    {
        var applicationId = Guid.NewGuid();
        await SeedAsync(applicationId, "0000", "0001");

        var documentId = Guid.NewGuid();
        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Documents.Add(CreateDocument(documentId));
            seedContext.DocumentPlacements.Add(new DocumentPlacementRecord
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ApplicationId = applicationId,
                SequenceNumber = "0001",
                CtdSection = "m5.3.7",
                Operation = "new",
                CreatedUtc = DateTime.UtcNow
            });
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = fixture.CreateDbContext();
        var transaction = new EfCoreApplicationDeletionTransaction(dbContext);

        // 编排顺序错误的真实形态：先删了序列，再去删仍被 placement 引用的文档。
        // 后者撞 Restrict 外键，PG 让整个事务失效，第一步的删除也必须一并回滚。
        await Assert.ThrowsAsync<PostgresException>(() => transaction.ExecuteAsync(async ct =>
        {
            var sequence = await dbContext.Sequences.SingleAsync(
                x => x.ApplicationId == applicationId && x.SequenceNumber == "0000", ct);
            dbContext.Sequences.Remove(sequence);
            await dbContext.SaveChangesAsync(ct);

            await dbContext.Database.ExecuteSqlRawAsync(
                "DELETE FROM documents WHERE \"Id\" = {0}",
                [documentId],
                ct);
        }));

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(2, await verifyContext.Sequences.CountAsync(x => x.ApplicationId == applicationId));
        Assert.True(await verifyContext.Documents.AnyAsync(x => x.Id == documentId));
    }

    private async Task SeedAsync(Guid applicationId, params string[] sequenceNumbers)
    {
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Applications.Add(new ApplicationRecord
        {
            Id = applicationId,
            ApplicationNumber = $"NDA-{Guid.NewGuid():N}",
            Region = "US",
            SponsorName = "Contoso Pharma",
            EctdTemplateKey = "us-ectd-3.2.2",
            WorkingDirectoryPath = $"C:/workspace/{applicationId:N}",
            CreatedUtc = DateTime.UtcNow
        });

        foreach (var sequenceNumber in sequenceNumbers)
        {
            dbContext.Sequences.Add(new SequenceRecord
            {
                ApplicationId = applicationId,
                SequenceNumber = sequenceNumber,
                SubmissionType = "original",
                Description = "seed sequence",
                CreatedUtc = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static DocumentRecord CreateDocument(Guid id) => new()
    {
        Id = id,
        FileName = "doc.pdf",
        MediaType = "application/pdf",
        FileSize = 1024,
        Sha256 = "sha256-value",
        Md5 = "md5-value",
        StoragePath = $"C:/workspace/{id:N}/doc.pdf",
        CreatedUtc = DateTime.UtcNow
    };
}
