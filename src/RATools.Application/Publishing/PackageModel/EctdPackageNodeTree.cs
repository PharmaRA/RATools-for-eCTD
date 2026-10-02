using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Application.Publishing.PackageModel;

// Pure package projection: never creates or repairs persisted workspace records.
public sealed class EctdPackageNodeTree
{
    private EctdPackageNodeTree(IReadOnlyList<EctdPackageNode> nodes, IReadOnlyList<EctdLeaf> leaves)
    {
        Nodes = nodes;
        Leaves = leaves;
    }

    public IReadOnlyList<EctdPackageNode> Nodes { get; }
    public IReadOnlyList<EctdLeaf> Leaves { get; }

    public static EctdPackageNodeTree Create(Guid applicationId, IEnumerable<EctdPackageNode> sourceNodes,
        IEnumerable<EctdLeaf> sourceLeaves, bool allowLegacyBinding)
    {
        var definitions = IchSectionDefinitions.Current;
        var nodes = new Dictionary<Guid, EctdPackageNode>();
        foreach (var node in sourceNodes)
            if (!nodes.TryAdd(node.NodeInstanceId, node with { Attributes = node.Attributes.ToFrozenDictionary(StringComparer.Ordinal) }))
                throw Error("DuplicateSequenceNode", "The package contains a duplicate node ID.", node);
        var leaves = new List<EctdLeaf>();
        foreach (var leaf in sourceLeaves)
        {
            if (leaf.SortOrder < 0) throw new EctdPackageNodeException("InvalidLeafOrder", "Leaf order must be nonnegative.", leaf.NodeInstanceId, leaf.PlacementId);
            if (leaf.NodeInstanceId is not null) { leaves.Add(leaf); continue; }
            if (!allowLegacyBinding)
                throw new EctdPackageNodeException("NodeBindingRequired", "Select a business node before publishing this leaf.", null, leaf.PlacementId, leaf.CtdSection);
            var matches = definitions.Definitions.Values.Where(definition => definition.SectionPath == leaf.CtdSection).ToArray();
            if (matches.Length != 1)
                throw new EctdPackageNodeException("NodeBindingRequired", $"Section '{leaf.CtdSection}' does not uniquely identify a schema node.", null, leaf.PlacementId, leaf.CtdSection);
            Guid EnsureLegacyNode(SectionDefinition definition)
            {
                // Even a currently unique repeatable group is not evidence of the
                // intended business identity of an old unbound placement.
                if (definition.Repeatable || definition.Attributes.Any(attribute => attribute.Required))
                    throw new EctdPackageNodeException("NodeBindingRequired", $"Section '{leaf.CtdSection}' needs an explicit business instance and its required attributes.", null, leaf.PlacementId, leaf.CtdSection);
                Guid? parent = definition.ParentDefinitionKey is { } key ? EnsureLegacyNode(definitions.Get(key)) : null;
                var existing = nodes.Values.Where(node => node.ParentInstanceId == parent && node.DefinitionKey == definition.DefinitionKey).ToArray();
                if (existing.Length > 1) throw Error("NonRepeatableNodeConflict", "A singleton section has more than one instance.", existing[0]);
                if (existing.Length == 1) return existing[0].NodeInstanceId;
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"legacy-package-v1/{applicationId:D}/{parent:D}/{definition.DefinitionKey}"))[..16]);
                nodes.Add(id, new EctdPackageNode(id, parent, definition.DefinitionKey, definitions.Version,
                    new Dictionary<string, string>().ToFrozenDictionary(), null, 0, NodeMetadataStatus.Complete));
                return id;
            }
            leaves.Add(leaf with { NodeInstanceId = EnsureLegacyNode(matches[0]) });
        }

        // Select the actual emitted tree. Empty UI groups do not require invented
        // output attributes and do not appear in the XML.
        var used = new HashSet<Guid>();
        foreach (var leaf in leaves)
        {
            var visited = new HashSet<Guid>();
            for (var id = leaf.NodeInstanceId; id is not null;)
            {
                if (!visited.Add(id.Value)) throw new EctdPackageNodeException("NodeParentCycle", "The package node chain contains a cycle.", id, leaf.PlacementId);
                if (!nodes.TryGetValue(id.Value, out var node))
                    throw new EctdPackageNodeException("SequenceNodeParentMissing", "The package must contain the leaf's node and its complete ancestor chain.", id, leaf.PlacementId);
                used.Add(id.Value);
                id = node.ParentInstanceId;
            }
        }
        var emitted = nodes.Values.Where(node => used.Contains(node.NodeInstanceId)).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
            if (!ids.Add(leaf.LeafId)) throw new EctdPackageNodeException("DuplicateXmlId", $"Duplicate ICH XML ID '{leaf.LeafId}'.", leaf.NodeInstanceId, leaf.PlacementId);
        foreach (var node in emitted)
        {
            if (node.NodeInstanceId == Guid.Empty || node.DefinitionVersion != definitions.Version ||
                !definitions.Definitions.TryGetValue(node.DefinitionKey, out var definition))
                throw Error("UnsupportedNodeSchema", "The node requires a supported, pinned definition schema.", node);
            var attributeIssues = definition.ValidateAttributes(node.Attributes);
            if (attributeIssues.Count > 0)
                throw Error("NodeMetadataIncomplete", $"Node {node.NodeInstanceId}: {string.Join(" | ", attributeIssues.Select(issue => issue.Message))}", node);
            if (node.MetadataStatus != NodeMetadataStatus.Complete || node.SortOrder < 0 ||
                definition.Kind == CtdNodeKind.Extension && string.IsNullOrWhiteSpace(node.Title))
                throw Error("NodeMetadataIncomplete", $"Node {node.NodeInstanceId} has incomplete or unresolved metadata.", node);
            if (node.ParentInstanceId is { } parent)
            {
                if (!definitions.Definitions.TryGetValue(nodes[parent].DefinitionKey, out var parentDefinition))
                    throw Error("UnsupportedNodeSchema", "The node's parent requires a supported definition schema.", nodes[parent]);
                if (definition.Kind == CtdNodeKind.Extension ? parentDefinition.ExtensionPolicy != NodeExtensionPolicy.Allowed : definition.ParentDefinitionKey != parentDefinition.DefinitionKey)
                    throw Error("InvalidNodeParent", "The node's parent is not permitted by the schema.", node);
            }
            else if (definition.ParentDefinitionKey is not null || definition.Kind == CtdNodeKind.Extension)
                throw Error("NodeParentRequired", "This node requires a parent.", node);
            if (node.Attributes.TryGetValue("ID", out var xmlId) && !ids.Add(xmlId))
                throw Error("DuplicateXmlId", $"Duplicate ICH XML ID '{xmlId}'.", node);
        }
        foreach (var group in emitted.GroupBy(node => (node.ParentInstanceId, node.DefinitionKey)))
        {
            var definition = definitions.Get(group.Key.DefinitionKey);
            if (group.Count() > 1 && !definition.Repeatable)
                throw Error("NonRepeatableNodeConflict", "A singleton section has more than one emitted instance.", group.First());
            if (definition.Kind == CtdNodeKind.Standard && group.GroupBy(node => System.Text.Json.JsonSerializer.Serialize(
                    definition.Attributes.Where(attribute => attribute.Identity).OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
                        .Select(attribute => node.Attributes.GetValueOrDefault(attribute.Name)).ToArray()), StringComparer.Ordinal).Any(items => items.Count() > 1))
                throw Error("AmbiguousNodeIdentity", "Equal sibling business identities require explicit mapping before publishing.", group.First());
        }
        foreach (var leaf in leaves)
        {
            var node = nodes[leaf.NodeInstanceId!.Value];
            var definition = definitions.Get(node.DefinitionKey);
            while (definition.Kind == CtdNodeKind.Extension)
            {
                node = nodes[node.ParentInstanceId!.Value];
                definition = definitions.Get(node.DefinitionKey);
            }
            if (definition.SectionPath != leaf.CtdSection || !definitions.Get(nodes[leaf.NodeInstanceId.Value].DefinitionKey).AllowsLeaves)
                throw new EctdPackageNodeException("PlacementNodeSectionMismatch", "The leaf's section or node content differs from the schema.", leaf.NodeInstanceId, leaf.PlacementId, leaf.CtdSection);
        }
        return new EctdPackageNodeTree(Array.AsReadOnly(emitted), leaves.AsReadOnly());
    }

    private static EctdPackageNodeException Error(string code, string message, EctdPackageNode node) => new(code, message, node.NodeInstanceId);
}
