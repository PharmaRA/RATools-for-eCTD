using Microsoft.EntityFrameworkCore;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Standards;

public sealed class RegionalMetadataPersistenceTests
{
    [Fact]
    public void StoredContextsEnforceApplicationSequenceAndPlacementOrNodeOwnership()
    {
        using var db = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql("Host=localhost;Database=offline_model").Options);
        var revision = db.Model.GetEntityTypes().SingleOrDefault(entity => entity.GetTableName() == "regional_metadata_revisions");
        Assert.NotNull(revision);
        var node = db.Model.GetEntityTypes().Single(entity => entity.GetTableName() == "regional_metadata_node_contexts");
        var document = db.Model.GetEntityTypes().Single(entity => entity.GetTableName() == "regional_metadata_document_contexts");
        Assert.Contains(node.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "sequence_nodes"
            && key.Properties.Select(property => property.Name).SequenceEqual(["ApplicationId", "SequenceNumber", "NodeInstanceId"]));
        Assert.Contains(document.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "document_placements"
            && key.Properties.Select(property => property.Name).SequenceEqual(["ApplicationId", "SequenceNumber", "PlacementId"]));
        foreach (var entity in new[] { node, document })
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType == revision
                && key.Properties.Select(property => property.Name).SequenceEqual(["ApplicationId", "SequenceNumber", "MetadataId"]));
    }
}
