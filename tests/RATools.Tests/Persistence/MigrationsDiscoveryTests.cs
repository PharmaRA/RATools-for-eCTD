using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

/// <summary>
/// 防复发守卫：20260608234000_AddUsRegionalAdminMetadata 曾因缺少 Designer/[Migration]
/// 特性而对 EF 不可见（dotnet ef migrations list 只显示 7/8），全新数据库缺 5 个列。
/// 这里断言"迁移源文件数 == EF 实际发现的迁移数"，任何再次丢失特性的迁移都会立即失败。
/// </summary>
public sealed class MigrationsDiscoveryTests
{
    private static IReadOnlyList<string> GetDiscoveredMigrationIds()
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        using var dbContext = new RAToolsDbContext(options);
        var migrationsAssembly = dbContext.GetService<IMigrationsAssembly>();
        return migrationsAssembly.Migrations.Keys.ToArray();
    }

    private static string[] GetMigrationSourceFileIds()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var migrationsDirectory = Path.Combine(
            repositoryRoot, "src", "RATools.Infrastructure", "Persistence", "EfCore", "Migrations");

        return Directory.GetFiles(migrationsDirectory, "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null
                && !name.EndsWith(".Designer", StringComparison.Ordinal)
                && !name.Contains("ModelSnapshot", StringComparison.Ordinal))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [Fact]
    public void EveryMigrationSourceFileIsDiscoveredByEfCore()
    {
        var discovered = GetDiscoveredMigrationIds();
        var sourceFiles = GetMigrationSourceFileIds();

        Assert.Equal(sourceFiles, discovered.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void UsRegionalAdminMetadataMigrationIsDiscovered()
    {
        var discovered = GetDiscoveredMigrationIds();

        Assert.Contains("20260608234000_AddUsRegionalAdminMetadata", discovered);
    }
}
