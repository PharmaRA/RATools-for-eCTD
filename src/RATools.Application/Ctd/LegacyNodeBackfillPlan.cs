using System.Security.Cryptography;
using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Common;
using RATools.Domain.Ctd;

namespace RATools.Application.Ctd;

public sealed record LegacyPlacementNodeInput(Guid PlacementId, string CtdSection, Guid? NodeInstanceId);

public sealed record LegacyNodeBackfillPlan(CtdNodeGraph Graph, IReadOnlyList<SequenceNode> Nodes,
    IReadOnlyDictionary<Guid, Guid> Bindings, IReadOnlyList<CtdBackfillDiagnostic> Diagnostics)
{
    public const string Version = "legacy-ctd-backfill-v1";

    public static LegacyNodeBackfillPlan Create(CtdSequenceWorkspace workspace, IReadOnlyList<LegacyPlacementNodeInput> placements)
    {
        var graph = new CtdNodeGraph(workspace.Graph.ApplicationId, workspace.Graph.Definitions, workspace.Graph.Nodes.Values);
        var sequenceNodes = workspace.Nodes.ToDictionary(node => node.NodeInstanceId);
        var bindings = new Dictionary<Guid, Guid>();
        var diagnostics = new List<CtdBackfillDiagnostic>();
        var bySection = graph.Definitions.Definitions.Values.Where(definition => definition.Kind == CtdNodeKind.Standard)
            .ToLookup(definition => definition.SectionPath!, StringComparer.OrdinalIgnoreCase);
        var validSequence = workspace.SequenceNumber.Length == 4 && workspace.SequenceNumber.All(character => character is >= '0' and <= '9');

        foreach (var placement in placements.OrderBy(placement => placement.PlacementId))
        {
            if (!validSequence)
            {
                diagnostics.Add(new(placement.PlacementId, "InvalidSequenceNumber", "Node backfill requires a four-digit sequence; the legacy record was retained."));
                continue;
            }
            if (placement.NodeInstanceId is { } currentId)
            {
                if (!sequenceNodes.TryGetValue(currentId, out var currentNode))
                    throw new CtdNodeConstraintException("PlacementNodeMissing", "A bound placement has no matching sequence node.", currentId);
                AddCompletionDiagnostic(placement.PlacementId, currentNode, diagnostics);
                continue;
            }
            var definitions = bySection[placement.CtdSection.Trim()].ToArray();
            if (definitions.Length != 1)
            {
                diagnostics.Add(new(placement.PlacementId, definitions.Length == 0 ? "SectionDefinitionUnavailable" : "AmbiguousSectionDefinition",
                    "The legacy section has no unique definition in this schema; retain the original placement for explicit mapping."));
                continue;
            }

            var ancestry = new Stack<SectionDefinition>();
            var definition = definitions[0];
            while (true)
            {
                ancestry.Push(definition);
                if (definition.ParentDefinitionKey is not { } parent) break;
                definition = graph.Definitions.Get(parent);
            }
            Guid? parentId = null;
            foreach (var item in ancestry)
            {
                // A repeated legacy branch is deliberately separate for every placement.
                // Section numbers alone cannot prove common manufacturer/product identity.
                var discriminator = item.Repeatable ? placement.PlacementId.ToString("D") : null;
                var id = StableId(graph.ApplicationId, parentId, item.DefinitionKey, discriminator);
                var existing = item.Repeatable ? graph.Nodes.GetValueOrDefault(id) : graph.Nodes.Values.SingleOrDefault(node =>
                    node.ParentInstanceId == parentId && node.DefinitionKey == item.DefinitionKey);
                if (existing is null)
                {
                    existing = CtdNodeInstance.Rehydrate(id, graph.ApplicationId, parentId, graph.Definitions,
                        item.DefinitionKey, new Dictionary<string, string>(),
                        item.Repeatable ? CtdIdentityStatus.Ambiguous : CtdIdentityStatus.Resolved);
                    graph.Add(existing);
                }
                if (!sequenceNodes.ContainsKey(existing.Id))
                    sequenceNodes.Add(existing.Id, new SequenceNode(graph, workspace.SequenceNumber, existing.Id, existing.IdentityAttributes));
                parentId = existing.Id;
            }
            bindings.Add(placement.PlacementId, parentId!.Value);
            AddCompletionDiagnostic(placement.PlacementId, sequenceNodes[parentId.Value], diagnostics);
        }
        return new(graph, sequenceNodes.Values.OrderBy(node => node.SortOrder).ThenBy(node => node.NodeInstanceId).ToArray(), bindings, diagnostics);
    }

    public static string InputDigest(IEnumerable<LegacyPlacementNodeInput> placements) => CanonicalJson.Digest(JsonSerializer.SerializeToElement(
        placements.OrderBy(placement => placement.PlacementId).Select(placement => new object?[]
        {
            placement.PlacementId.ToString("D"), placement.CtdSection, placement.NodeInstanceId?.ToString("D")
        }).ToArray()));

    private static Guid StableId(Guid applicationId, Guid? parent, string definitionKey, string? discriminator) =>
        new(SHA256.HashData(CanonicalJson.Encode(JsonSerializer.SerializeToElement(new object?[]
        {
            Version, applicationId.ToString("D"), parent?.ToString("D"), definitionKey, discriminator
        }))).AsSpan(0, 16));

    private static void AddCompletionDiagnostic(Guid placementId, SequenceNode node, List<CtdBackfillDiagnostic> diagnostics)
    {
        if (node.MetadataStatus != NodeMetadataStatus.Complete)
            diagnostics.Add(new(placementId, "LegacyNodeIdentityUnresolved", "This node or its ancestor lacks verified business identity; complete an explicit mapping before publication."));
    }
}
