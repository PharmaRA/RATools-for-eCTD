using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

public sealed class EfCoreModelConstraintTests
{
    [Fact]
    public void ApplicationRecord_HasIndexOnApplicationNumber()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(ApplicationRecord));

        Assert.Contains(entityType!.GetIndexes(), x => HasProperties(x, nameof(ApplicationRecord.ApplicationNumber)));
    }

    [Fact]
    public void DocumentPlacementRecord_HasExpectedForeignKeys()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(DocumentPlacementRecord));

        AssertForeignKey(
            entityType!,
            typeof(ApplicationRecord),
            DeleteBehavior.Cascade,
            nameof(DocumentPlacementRecord.ApplicationId));
        AssertForeignKey(
            entityType!,
            typeof(SequenceRecord),
            DeleteBehavior.Cascade,
            nameof(DocumentPlacementRecord.ApplicationId),
            nameof(DocumentPlacementRecord.SequenceNumber));
        AssertForeignKey(
            entityType!,
            typeof(DocumentRecord),
            DeleteBehavior.Restrict,
            nameof(DocumentPlacementRecord.DocumentId));
    }

    [Fact]
    public void DocumentPlacementRecord_HasExpectedIndexes()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(DocumentPlacementRecord));

        Assert.Contains(entityType!.GetIndexes(), x => HasProperties(
            x,
            nameof(DocumentPlacementRecord.ApplicationId),
            nameof(DocumentPlacementRecord.SequenceNumber)));
        Assert.Contains(entityType.GetIndexes(), x => HasProperties(x, nameof(DocumentPlacementRecord.DocumentId)));
    }

    [Fact]
    public void PublishJobRecord_HasExpectedIndexes()
    {
        using var dbContext = CreateDbContext();
        var entityType = dbContext.Model.FindEntityType(typeof(PublishJobRecord));

        Assert.Contains(entityType!.GetIndexes(), x => HasProperties(
            x,
            nameof(PublishJobRecord.ApplicationId),
            nameof(PublishJobRecord.CreatedUtc)));
        Assert.Contains(entityType.GetIndexes(), x => HasProperties(
            x,
            nameof(PublishJobRecord.ApplicationId),
            nameof(PublishJobRecord.SequenceNumber),
            nameof(PublishJobRecord.CreatedUtc)));
        Assert.Contains(entityType.GetIndexes(), x => HasProperties(
            x,
            nameof(PublishJobRecord.ApplicationId),
            nameof(PublishJobRecord.SequenceNumber),
            nameof(PublishJobRecord.Status)));

        var activeJobIndex = Assert.Single(entityType.GetIndexes(), x => HasProperties(
            x,
            nameof(PublishJobRecord.ApplicationId),
            nameof(PublishJobRecord.SequenceNumber)));
        Assert.True(activeJobIndex.IsUnique);
        Assert.Equal("\"Status\" IN ('Pending', 'Running')", activeJobIndex.GetFilter());
    }

    private static RAToolsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase($"ratools-model-constraints-{Guid.NewGuid():N}")
            .Options;

        return new RAToolsDbContext(options);
    }

    private static void AssertForeignKey(
        IEntityType entityType,
        Type principalType,
        DeleteBehavior deleteBehavior,
        params string[] propertyNames)
    {
        var foreignKey = Assert.Single(entityType.GetForeignKeys(), x =>
            x.PrincipalEntityType.ClrType == principalType && HasProperties(x, propertyNames));

        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
    }

    private static bool HasProperties(IReadOnlyIndex index, params string[] propertyNames) =>
        index.Properties.Select(x => x.Name).SequenceEqual(propertyNames);

    private static bool HasProperties(IReadOnlyForeignKey foreignKey, params string[] propertyNames) =>
        foreignKey.Properties.Select(x => x.Name).SequenceEqual(propertyNames);
}
