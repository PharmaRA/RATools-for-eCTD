using System.Text.Json;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Infrastructure.Persistence.EfCore;

internal static class CtdRecordMapping
{
    public static CtdNodeInstance ToDomain(this CtdNodeInstanceRecord row)
    {
        if (row.DefinitionVersion != IchSectionDefinitions.Version || row.IdentityComparisonVersion != IchSectionDefinitions.Current.IdentityComparisonVersion)
            throw new CtdNodeConstraintException("UnsupportedNodeSchema", "This node requires its original definition/comparison schema.", row.Id);
        var node = CtdNodeInstance.Rehydrate(row.Id, row.ApplicationId, row.ParentInstanceId, IchSectionDefinitions.Current,
            row.DefinitionKey, Attributes(row.IdentityAttributesJson), Enum.Parse<CtdIdentityStatus>(row.IdentityStatus), row.IdentityKey);
        var definition = IchSectionDefinitions.Current.Get(node.DefinitionKey);
        if (row.Kind != node.Kind.ToString() || row.Repeatable != definition.Repeatable)
            throw new CtdNodeConstraintException("NodeDefinitionMismatch", "Persisted node shape differs from its definition.", row.Id);
        return node;
    }

    public static CtdNodeInstanceRecord ToRecord(this CtdNodeInstance node, CtdNodeGraph graph) => new()
    {
        Id = node.Id, ApplicationId = node.ApplicationId, ParentInstanceId = node.ParentInstanceId,
        DefinitionVersion = node.DefinitionVersion, DefinitionKey = node.DefinitionKey,
        Repeatable = graph.Definitions.Get(node.DefinitionKey).Repeatable, Kind = node.Kind.ToString(), IdentityKey = node.IdentityKey,
        IdentityComparisonVersion = node.IdentityComparisonVersion, IdentityStatus = node.IdentityStatus.ToString(),
        IdentityAttributesJson = JsonSerializer.Serialize(node.IdentityAttributes)
    };

    public static SequenceNode ToDomain(this SequenceNodeRecord row, CtdNodeGraph graph)
    {
        var result = new SequenceNode(graph, row.SequenceNumber, row.NodeInstanceId, Attributes(row.AttributesJson),
            row.Title, row.SortOrder, row.StorageSegment);
        if (row.ApplicationId != graph.ApplicationId || row.DefinitionVersion != result.DefinitionVersion ||
            row.ParentNodeInstanceId != graph.Get(row.NodeInstanceId).ParentInstanceId ||
            row.CtdSection != result.CtdSection || row.MetadataStatus != result.MetadataStatus.ToString())
            throw new CtdNodeConstraintException("SequenceNodeMetadataMismatch", "Stored sequence metadata differs from its node context.", row.NodeInstanceId);
        return result;
    }

    public static SequenceNodeRecord ToRecord(this SequenceNode node, CtdNodeGraph graph) => new()
    {
        ApplicationId = node.ApplicationId, SequenceNumber = node.SequenceNumber, NodeInstanceId = node.NodeInstanceId,
        ParentNodeInstanceId = graph.Get(node.NodeInstanceId).ParentInstanceId, DefinitionVersion = node.DefinitionVersion,
        CtdSection = node.CtdSection, AttributesJson = JsonSerializer.Serialize(node.Attributes), Title = node.Title,
        SortOrder = node.SortOrder, StorageSegment = node.StorageSegment, MetadataStatus = node.MetadataStatus.ToString()
    };

    private static Dictionary<string, string> Attributes(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? throw new CtdNodeConstraintException("InvalidStoredNodeAttributes", "Stored node attributes must be a JSON object.");
}
