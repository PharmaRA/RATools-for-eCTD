using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Domain.Auditing;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Persistence;

/// <summary>
/// 分页查询的语义必须在两个实现上一致：EF（真 SQL 翻译）与 InMemory（开发/测试宿主）。
/// 每个用例都对两者跑一遍，避免出现"只有某一个 provider 正确"的漂移。
/// </summary>
public sealed class AuditLogRepositoryQueryTests
{
    [Fact]
    public async Task QueryAsync_OrdersNewestFirstAndReportsFilteredTotal()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(Page: 1, PageSize: 2));

            Assert.Equal(4, totalCount);
            Assert.Equal(2, items.Count);
            Assert.Collection(
                items,
                first => Assert.Equal("Deleted", first.Action),
                second => Assert.Equal("Completed", second.Action));
        });
    }

    [Fact]
    public async Task QueryAsync_ReturnsRequestedPage()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(Page: 2, PageSize: 2));

            // 总数是过滤后的全集，不随页码变化。
            Assert.Equal(4, totalCount);
            Assert.Collection(
                items,
                first => Assert.Equal("Failed", first.Action),
                second => Assert.Equal("Created", second.Action));
        });
    }

    [Fact]
    public async Task QueryAsync_FiltersByEntityType()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(EntityType: "PublishJob"));

            Assert.Equal(2, totalCount);
            Assert.All(items, x => Assert.Equal("PublishJob", x.EntityType));
        });
    }

    [Fact]
    public async Task QueryAsync_FiltersByEntityId()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(EntityId: "job-1"));

            Assert.Equal(2, totalCount);
            Assert.All(items, x => Assert.Equal("job-1", x.EntityId));
        });
    }

    [Fact]
    public async Task QueryAsync_FiltersByAction()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(Action: "Completed"));

            var entry = Assert.Single(items);
            Assert.Equal(1, totalCount);
            Assert.Equal("Completed", entry.Action);
        });
    }

    [Fact]
    public async Task QueryAsync_FiltersByCreatedRangeInclusively()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(
                CreatedFromUtc: BaseTime.AddHours(1),
                CreatedToUtc: BaseTime.AddHours(2)));

            Assert.Equal(2, totalCount);
            Assert.Collection(
                items,
                first => Assert.Equal("Completed", first.Action),
                second => Assert.Equal("Failed", second.Action));
        });
    }

    [Fact]
    public async Task QueryAsync_CombinesFilters()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(
                EntityType: "PublishJob",
                EntityId: "job-1",
                Action: "Completed"));

            Assert.Equal(1, totalCount);
            Assert.Single(items);
        });
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmptyPageBeyondTotal()
    {
        await RunOnBothRepositoriesAsync(async repository =>
        {
            await SeedAsync(repository);

            var (items, totalCount) = await repository.QueryAsync(new AuditLogQuery(Page: 99, PageSize: 20));

            // 越界页返回空条目但仍报告真实总数，供前端渲染分页器。
            Assert.Empty(items);
            Assert.Equal(4, totalCount);
        });
    }

    private static readonly DateTime BaseTime = new(2026, 7, 26, 8, 0, 0, DateTimeKind.Utc);

    private static async Task SeedAsync(IAuditLogRepository repository)
    {
        await repository.AddAsync(Entry("PublishJob", "job-1", "Created", BaseTime));
        await repository.AddAsync(Entry("PublishJob", "job-1", "Completed", BaseTime.AddHours(2)));
        await repository.AddAsync(Entry("SequenceValidation", "app-1:0000", "Failed", BaseTime.AddHours(1)));
        await repository.AddAsync(Entry("PublishJobArtifact", "job-1:report", "Deleted", BaseTime.AddHours(3)));
    }

    private static AuditLogEntry Entry(string entityType, string entityId, string action, DateTime createdUtc)
        => AuditLogEntry.Rehydrate(Guid.NewGuid(), entityType, entityId, action, "tester", null, createdUtc);

    private static async Task RunOnBothRepositoriesAsync(Func<IAuditLogRepository, Task> assert)
    {
        await assert(new InMemoryAuditLogRepository());

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new RAToolsDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        await assert(new EfCoreAuditLogRepository(dbContext));
    }
}
