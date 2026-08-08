using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;
using Testcontainers.PostgreSql;

namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 整个测试运行期共享一个 postgres:16 容器。镜像与 <c>.github/workflows/smoke.yml</c> 一致，
/// 让 CI 复用同一份镜像层。容器起好后跑一次 <c>MigrateAsync()</c>——这同时也是
/// 迁移链能否落在真实 PostgreSQL 上的首个自动化验证（此前只有 smoke 经 API 间接覆盖）。
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;
    private readonly string? _externalConnectionString;

    public PostgresFixture()
    {
        _externalConnectionString = PostgresTestEnvironment.ExternalConnectionString;
        if (_externalConnectionString is not null)
        {
            // 显式指定了实例：不起容器。
            return;
        }

        if (!PostgresTestEnvironment.IsDockerAvailable)
        {
            // 无 Docker 也无外部实例：用例全部 Skip，Initialize/Dispose 均为空操作。
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ratools_constraints")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    private string ConnectionString =>
        _externalConnectionString
        ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("没有可用的 PostgreSQL：本机无 Docker 且未指定实例时，用例应当已被 Skip。");

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
        }
        else if (_externalConnectionString is null)
        {
            return;
        }

        // 迁移只跑一次；各用例靠独立的 GUID / 申请号互不干扰。
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public RAToolsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new RAToolsDbContext(options);
    }
}

/// <summary>
/// 把所有真实 PostgreSQL 用例归入同一 collection：共享一个容器，且彼此串行执行。
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
