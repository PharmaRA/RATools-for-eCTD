using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Publishing;

public sealed class PublishBindingMigrationTests
{
    [Fact]
    public void DowngradeBlocksConcurrentWritersBeforeCheckingForCommittedBindings()
    {
        using var context = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_check").Options);
        var script = context.GetService<IMigrator>().GenerateScript("20261104090000_BindPublishValidationRun",
            "20261028022928_GuardFinalizedEvidenceAppendOnly");
        var transaction = script.IndexOf("START TRANSACTION", StringComparison.Ordinal);
        var writeLock = script.IndexOf("LOCK TABLE publish_jobs IN ACCESS EXCLUSIVE MODE", StringComparison.Ordinal);
        var check = script.IndexOf("IF EXISTS (SELECT 1 FROM publish_jobs", StringComparison.Ordinal);
        var drop = script.IndexOf("DROP COLUMN", StringComparison.Ordinal);
        var commit = script.IndexOf("COMMIT", StringComparison.Ordinal);
        Assert.True(transaction >= 0 && writeLock > transaction && check > writeLock && drop > check && commit > drop);
    }

    [Fact]
    public void PersistedBindingHasADiscoverableMigrationAndTheSnapshotMatchesTheCurrentModel()
    {
        using var context = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_check").Options);
        var migrations = context.GetService<IMigrationsAssembly>();
        Assert.Contains("20261104090000_BindPublishValidationRun", migrations.Migrations.Keys);
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshot = initializer.Initialize(migrations.ModelSnapshot!.Model, designTime: true);
        var current = context.GetService<IDesignTimeModel>().Model;
        Assert.False(context.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()));
        var binding = current.FindEntityType(typeof(PublishJobRecord))!.FindProperty(nameof(PublishJobRecord.PackageValidationRunId));
        Assert.NotNull(binding);
        Assert.True(binding.IsNullable);
        Assert.Equal(typeof(Guid?), binding.ClrType);
    }
}
