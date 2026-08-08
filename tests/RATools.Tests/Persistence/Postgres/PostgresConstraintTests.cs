using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 约束的**边界语义**验证：冲突必须被数据库拒绝。
/// <c>EfCoreModelConstraintTests</c> 只断言 EF 模型元数据（索引/外键声明存在），
/// InMemory provider 根本不强制唯一索引与外键，SQLite 的 <c>EnsureCreated()</c>
/// 也拿不到写在迁移里的原生 SQL 索引（<c>lower("ApplicationNumber")</c> 就是其一）。
/// 因此这里全部走真实 PostgreSQL + 完整迁移链。
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresConstraintTests(PostgresFixture fixture)
{
    private const string UniqueViolation = "23505";

    /// <summary>
    /// PostgreSQL 对 <c>ON DELETE RESTRICT</c> 抛 23001 restrict_violation，
    /// 而 <c>NO ACTION</c> 抛的才是 23503 foreign_key_violation。
    /// 断言 23001 因此顺带证明了该外键确实是 RESTRICT（不可延迟到事务提交时才检查）。
    /// </summary>
    private const string RestrictViolation = "23001";

    [RequiresPostgresFact]
    public async Task ApplicationNumber_UniqueIndexIgnoresCase()
    {
        var applicationNumber = NewApplicationNumber().ToUpperInvariant();

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Applications.Add(CreateApplication(Guid.NewGuid(), applicationNumber));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            // 仅大小写不同的申请号：迁移里的 lower() 唯一索引必须拦下。
            dbContext.Applications.Add(CreateApplication(Guid.NewGuid(), applicationNumber.ToLowerInvariant()));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

            Assert.Equal(UniqueViolation, GetPostgresException(exception).SqlState);
        }
    }

    [RequiresPostgresFact]
    public async Task PublishJobs_PartialUniqueIndexBlocksSecondActiveJobButAllowsRetryAfterFailure()
    {
        var applicationId = Guid.NewGuid();
        const string sequenceNumber = "0000";
        await SeedApplicationWithSequenceAsync(applicationId, sequenceNumber);

        Guid firstJobId;
        await using (var dbContext = fixture.CreateDbContext())
        {
            var job = CreatePublishJob(applicationId, sequenceNumber, "Pending");
            firstJobId = job.Id;
            dbContext.PublishJobs.Add(job);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            // 同一 app+seq 的第二个 Pending：部分唯一索引必须拒绝（防重复发布）。
            dbContext.PublishJobs.Add(CreatePublishJob(applicationId, sequenceNumber, "Pending"));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

            Assert.Equal(UniqueViolation, GetPostgresException(exception).SqlState);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var job = await dbContext.PublishJobs.SingleAsync(x => x.Id == firstJobId);
            job.Status = "Failed";
            job.FailureReason = "Recovered at startup: process restarted while job was queued/executing.";
            job.CompletedUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            // 首个作业转 Failed 后离开索引过滤条件，同序列必须可以重新发布——
            // 幽灵作业启动回收（4d7c231）整个机制就依赖这条语义成立。
            dbContext.PublishJobs.Add(CreatePublishJob(applicationId, sequenceNumber, "Pending"));

            await dbContext.SaveChangesAsync();

            Assert.Equal(1, await dbContext.PublishJobs.CountAsync(x =>
                x.ApplicationId == applicationId && x.Status == "Pending"));
        }
    }

    [RequiresPostgresFact]
    public async Task DeletingApplication_CascadesToSequencesAndPlacementsButKeepsDocument()
    {
        var applicationId = Guid.NewGuid();
        const string sequenceNumber = "0001";
        await SeedApplicationWithSequenceAsync(applicationId, sequenceNumber);

        var documentId = Guid.NewGuid();
        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Documents.Add(CreateDocument(documentId));
            dbContext.DocumentPlacements.Add(CreatePlacement(applicationId, sequenceNumber, documentId));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            // 走原生 SQL 删除，确保级联是**数据库**做的，而不是 EF 在内存里替我们做掉。
            var deleted = await dbContext.Database.ExecuteSqlRawAsync(
                "DELETE FROM applications WHERE \"Id\" = {0}",
                applicationId);

            Assert.Equal(1, deleted);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            Assert.False(await dbContext.Sequences.AnyAsync(x => x.ApplicationId == applicationId));
            Assert.False(await dbContext.DocumentPlacements.AnyAsync(x => x.ApplicationId == applicationId));

            // 文档不是申请的子行：级联不应波及它（删除后成为可被孤儿扫描发现的独立行）。
            Assert.True(await dbContext.Documents.AnyAsync(x => x.Id == documentId));
        }
    }

    [RequiresPostgresFact]
    public async Task DeletingDocument_IsRejectedWhilePlacementsStillReferenceIt()
    {
        var applicationId = Guid.NewGuid();
        const string sequenceNumber = "0002";
        await SeedApplicationWithSequenceAsync(applicationId, sequenceNumber);

        var documentId = Guid.NewGuid();
        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Documents.Add(CreateDocument(documentId));
            dbContext.DocumentPlacements.Add(CreatePlacement(applicationId, sequenceNumber, documentId));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            // placement→document 是 Restrict：仍被引用的文档不得删除，
            // 否则包构建期会拿到指向空洞的 placement。
            // 原生 SQL 直接抛 PostgresException（DbUpdateException 只在 SaveChanges 路径包装）。
            var exception = await Assert.ThrowsAsync<PostgresException>(() => dbContext.Database.ExecuteSqlRawAsync(
                "DELETE FROM documents WHERE \"Id\" = {0}",
                documentId));

            Assert.Equal(RestrictViolation, exception.SqlState);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            Assert.True(await dbContext.Documents.AnyAsync(x => x.Id == documentId));
        }
    }

    private async Task SeedApplicationWithSequenceAsync(Guid applicationId, string sequenceNumber)
    {
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Applications.Add(CreateApplication(applicationId, NewApplicationNumber()));
        dbContext.Sequences.Add(new SequenceRecord
        {
            ApplicationId = applicationId,
            SequenceNumber = sequenceNumber,
            SubmissionType = "original",
            Description = "seed sequence",
            CreatedUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static string NewApplicationNumber() => $"NDA-{Guid.NewGuid():N}";

    private static ApplicationRecord CreateApplication(Guid id, string applicationNumber) => new()
    {
        Id = id,
        ApplicationNumber = applicationNumber,
        Region = "US",
        SponsorName = "Contoso Pharma",
        EctdTemplateKey = "us-ectd-3.2.2",
        WorkingDirectoryPath = $"C:/workspace/{id:N}",
        CreatedUtc = DateTime.UtcNow
    };

    private static PublishJobRecord CreatePublishJob(Guid applicationId, string sequenceNumber, string status) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        SequenceNumber = sequenceNumber,
        Status = status,
        CreatedUtc = DateTime.UtcNow
    };

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

    private static DocumentPlacementRecord CreatePlacement(Guid applicationId, string sequenceNumber, Guid documentId) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = documentId,
        ApplicationId = applicationId,
        SequenceNumber = sequenceNumber,
        CtdSection = "m5.3.7",
        Operation = "new",
        Title = "Seed placement",
        CreatedUtc = DateTime.UtcNow
    };

    private static PostgresException GetPostgresException(DbUpdateException exception)
    {
        // 断言 SqlState 而不是异常文本：证明是**数据库**拒绝的，且不受 locale 影响。
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);

        return postgresException;
    }
}
