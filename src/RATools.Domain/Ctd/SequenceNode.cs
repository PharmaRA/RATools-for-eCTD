using System.Collections.Frozen;
using System.Xml;
using RATools.Domain.Common;

namespace RATools.Domain.Ctd;

public sealed class SequenceNode
{
    public SequenceNode(CtdNodeGraph graph, string sequenceNumber, Guid nodeInstanceId,
        IReadOnlyDictionary<string, string> attributes, string? title = null, int sortOrder = 0, string? storageSegment = null)
    {
        if (sequenceNumber is null || sequenceNumber.Length != 4 || sequenceNumber.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("A sequence number must contain exactly four ASCII digits.", nameof(sequenceNumber));
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);
        var node = graph.Get(nodeInstanceId);
        var definition = graph.Definitions.Get(node.DefinitionKey);
        var errors = definition.ValidateAttributes(attributes);
        if (errors.Any(error => error.Code != "RequiredNodeAttributeMissing"))
            throw new CtdNodeConstraintException("InvalidNodeAttributes", string.Join(" | ", errors.Select(error => error.Message)), nodeInstanceId);
        var exact = attributes.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var attribute in definition.Attributes.Where(attribute => attribute.Identity))
        {
            if (exact.ContainsKey(attribute.Name) != node.IdentityAttributes.ContainsKey(attribute.Name) ||
                exact.GetValueOrDefault(attribute.Name) != node.IdentityAttributes.GetValueOrDefault(attribute.Name))
                throw new CtdNodeConstraintException("NodeIdentityChangeRequiresNewInstance", "Identity attributes cannot be changed on an existing business instance.", nodeInstanceId);
        }
        if (title is not null) XmlConvert.VerifyXmlChars(title);
        ApplicationId = graph.ApplicationId;
        SequenceNumber = sequenceNumber;
        NodeInstanceId = nodeInstanceId;
        DefinitionVersion = graph.Definitions.Version;
        Attributes = exact;
        Title = title;
        SortOrder = sortOrder;
        StorageSegment = storageSegment is null ? null : PortablePathSegment.NormalizeAndValidate(storageSegment, nameof(storageSegment));
        CtdSection = graph.GetSectionPath(nodeInstanceId);
        AllowsLeaves = definition.AllowsLeaves;
        MetadataStatus = errors.Count > 0 || (node.Kind == CtdNodeKind.Extension && string.IsNullOrWhiteSpace(title))
            ? NodeMetadataStatus.NeedsMetadataCompletion
            : graph.HasUnresolvedIdentity(nodeInstanceId) ? NodeMetadataStatus.LegacyUnresolved : NodeMetadataStatus.Complete;
    }

    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    public Guid NodeInstanceId { get; }
    public string DefinitionVersion { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public string? Title { get; }
    public int SortOrder { get; }
    public string? StorageSegment { get; }
    public NodeMetadataStatus MetadataStatus { get; }
    public string CtdSection { get; }
    public bool AllowsLeaves { get; }
}
