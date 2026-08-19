using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 连上 <c>RATOOLS_TEST_POSTGRES</c> 指定的实例并验证独立迁移作业已经把 schema
/// 更新到当前版本。测试进程不再隐式迁移数据库，避免掩盖部署顺序错误。
/// 各用例靠独立 GUID / 申请号互不干扰。
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string? _connectionString = PostgresTestEnvironment.ConnectionString;

    public async Task InitializeAsync()
    {
        if (_connectionString is null)
        {
            // 无可用实例：用例已在发现阶段全部 Skip，这里不做任何事。
            return;
        }

        await using var dbContext = CreateDbContext();
        var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
        if (pendingMigrations.Length > 0)
        {
            throw new InvalidOperationException(
                "PostgreSQL test database was not migrated before the test run. "
                + $"Pending migrations: {string.Join(", ", pendingMigrations)}");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public RAToolsDbContext CreateDbContext()
    {
        var connectionString = _connectionString
            ?? throw new InvalidOperationException(
                $"没有可用的 PostgreSQL：未设置 {PostgresTestEnvironment.ConnectionStringVariable} 时用例应当已被 Skip。");

        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RAToolsDbContext(options);
    }
}

/// <summary>
/// 把所有真实 PostgreSQL 用例归入同一 collection：共享一次迁移，且彼此串行执行。
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
