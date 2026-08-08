using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 连上 <c>RATOOLS_TEST_POSTGRES</c> 指定的实例并跑一次 <c>MigrateAsync()</c>——
/// 这同时也是迁移链能否落在真实 PostgreSQL 上的首个自动化验证
/// （此前只有 smoke 经 API 间接覆盖）。各用例靠独立 GUID / 申请号互不干扰。
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
        await dbContext.Database.MigrateAsync();
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
