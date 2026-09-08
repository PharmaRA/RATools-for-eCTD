using RATools.Application.Ctd;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;

namespace RATools.Tests.Domain;

public sealed class CtdNodeInstanceTests
{
    private const string Quality = "m3-quality";
    private const string Body = "m3-2-body-of-data";
    private const string Substance = "m3-2-s-drug-substance";
    private const string Control = "m3-2-s-4-control-of-drug-substance";
    private const string Specification = "m3-2-s-4-1-specification";
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    [Fact]
    public void SameSectionUnderAlphaAndBetaHasDistinctStableBusinessIdentity()
    {
        var (graph, body) = CreateGraph();
        var alpha = graph.Create(Substance, body.Id, Attributes("Alpha"));
        var beta = graph.Create(Substance, body.Id, Attributes("Beta"));
        var alphaSpec = SpecificationNode(graph, alpha);
        var betaSpec = SpecificationNode(graph, beta);
        Assert.NotEqual(alpha.IdentityKey, beta.IdentityKey);
        Assert.NotEqual(alphaSpec.IdentityKey, betaSpec.IdentityKey);
        Assert.Equal(graph.GetSectionPath(alphaSpec.Id), graph.GetSectionPath(betaSpec.Id));
        var restored = CtdNodeInstance.Rehydrate(alpha.Id, graph.ApplicationId, body.Id, graph.Definitions,
            Substance, Attributes("Alpha"), expectedIdentityKey: alpha.IdentityKey);
        Assert.Equal(alpha.IdentityKey, restored.IdentityKey);
        Assert.Equal(alpha.Id, restored.Id);
        AssertCode("NodeIdentityMismatch", () => CtdNodeInstance.Rehydrate(alpha.Id, graph.ApplicationId, body.Id,
            graph.Definitions, Substance, Attributes("Beta"), expectedIdentityKey: alpha.IdentityKey));
    }

    [Fact]
    public void DuplicateBusinessIdentitiesAndNonrepeatableRootsAreRejectedAtomically()
    {
        var (graph, body) = CreateGraph();
        graph.Create(Substance, body.Id, Attributes("Alpha"));
        var count = graph.Nodes.Count;
        AssertCode("NodeIdentityConflict", () => graph.Create(Substance, body.Id, Attributes("Alpha")));
        AssertCode("NonRepeatableNodeConflict", () => graph.Create(Quality, null, Empty));
        AssertCode("NonRepeatableNodeConflict", () => graph.Create(Body, body.ParentInstanceId, Empty));
        Assert.Equal(count, graph.Nodes.Count);
        Assert.NotEqual(graph.Create(Substance, body.Id, Attributes("alpha")).IdentityKey,
            graph.Create(Substance, body.Id, Attributes(" Alpha ")).IdentityKey);
    }

    [Fact]
    public void WrongParentsAndApplicationsCannotEnterTheGraph()
    {
        var (graph, body) = CreateGraph();
        AssertCode("InvalidNodeParent", () => graph.Create(Substance, body.ParentInstanceId, Attributes("Alpha")));
        AssertCode("NodeParentRequired", () => graph.Create(Substance, null, Attributes("Alpha")));
        AssertCode("NodeNotFound", () => graph.Create(Substance, Guid.NewGuid(), Attributes("Alpha")));
        AssertCode("InvalidNodeParent", () => graph.Create("node-extension", body.Id, Empty));
        var foreign = CtdNodeInstance.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), null, graph.Definitions, Quality, Empty);
        AssertCode("NodeApplicationMismatch", () => _ = new CtdNodeGraph(graph.ApplicationId, graph.Definitions, [foreign]));
    }

    [Fact]
    public void RestoringExtensionCyclesAndSelfParentsIsRejected()
    {
        var applicationId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var schema = IchSectionDefinitions.Current;
        var first = CtdNodeInstance.Rehydrate(firstId, applicationId, secondId, schema, "node-extension", Empty);
        var second = CtdNodeInstance.Rehydrate(secondId, applicationId, firstId, schema, "node-extension", Empty);
        AssertCode("NodeParentCycle", () => _ = new CtdNodeGraph(applicationId, schema, [first, second]));
        var self = CtdNodeInstance.Rehydrate(firstId, applicationId, firstId, schema, "node-extension", Empty);
        AssertCode("NodeParentCycle", () => _ = new CtdNodeGraph(applicationId, schema, [self]));
    }

    [Fact]
    public void IncompleteAndAmbiguousImportsRemainVisibleWithoutBecomingPublishable()
    {
        var (graph, body) = CreateGraph();
        AssertCode("InvalidNodeAttributes", () => graph.Create(Substance, body.Id, Empty));
        var missing = CtdNodeInstance.Rehydrate(Guid.NewGuid(), graph.ApplicationId, body.Id, graph.Definitions,
            Substance, new Dictionary<string, string> { ["substance"] = "Drug A" }, CtdIdentityStatus.MissingMetadata);
        var first = CtdNodeInstance.Rehydrate(Guid.NewGuid(), graph.ApplicationId, body.Id, graph.Definitions, Substance, Attributes("Alpha"), CtdIdentityStatus.Ambiguous);
        var second = CtdNodeInstance.Rehydrate(Guid.NewGuid(), graph.ApplicationId, body.Id, graph.Definitions, Substance, Attributes("Alpha"), CtdIdentityStatus.Ambiguous);
        var restored = new CtdNodeGraph(graph.ApplicationId, graph.Definitions, graph.Nodes.Values.Concat([missing, first, second]));
        Assert.Equal(first.IdentityKey, second.IdentityKey);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(NodeMetadataStatus.NeedsMetadataCompletion, new SequenceNode(restored, "0000", missing.Id, missing.IdentityAttributes).MetadataStatus);
        Assert.Equal(NodeMetadataStatus.LegacyUnresolved, new SequenceNode(restored, "0000", first.Id, Attributes("Alpha")).MetadataStatus);
        var spec = SpecificationNode(restored, second);
        Assert.Equal(NodeMetadataStatus.LegacyUnresolved, new SequenceNode(restored, "0000", spec.Id, Empty).MetadataStatus);
        AssertCode("NodeIdentityChangeRequiresNewInstance", () => _ = new SequenceNode(restored, "0000", missing.Id, Attributes("Alpha")));
    }

    [Fact]
    public void SequenceMetadataDoesNotChangeHistoricalNodesOrIdentityAttributes()
    {
        var (graph, body) = CreateGraph();
        var values = Attributes("Alpha");
        var instance = graph.Create(Substance, body.Id, values);
        var original = new SequenceNode(graph, "0000", instance.Id, values, "Original title", 10, "drug-a-alpha");
        var later = new SequenceNode(graph, "0001", instance.Id, Attributes("Alpha"), "New title", 20, "drug-a-alpha");
        values["manufacturer"] = "Beta";
        Assert.Equal("Alpha", original.Attributes["manufacturer"]);
        Assert.Equal("Alpha", instance.IdentityAttributes["manufacturer"]);
        Assert.Equal("Original title", original.Title);
        Assert.Equal(10, original.SortOrder);
        Assert.Equal(original.NodeInstanceId, later.NodeInstanceId);
        Assert.Equal(original.StorageSegment, later.StorageSegment);
        AssertCode("NodeIdentityChangeRequiresNewInstance", () => _ = new SequenceNode(graph, "0001", instance.Id, Attributes("Beta")));
        AssertCode("NodeIdentityChangeRequiresNewInstance", () => _ = new SequenceNode(graph, "0001", instance.Id, Empty));
        Assert.Throws<ArgumentException>(() => new SequenceNode(graph, "1", instance.Id, Attributes("Alpha")));
        Assert.Throws<ArgumentException>(() => new SequenceNode(graph, "0001", instance.Id, Attributes("Alpha"), storageSegment: "../outside"));
    }

    [Fact]
    public void ExtensionTitlesAreSequenceValuesNotStableBusinessKeys()
    {
        var (graph, body) = CreateGraph();
        var spec = SpecificationNode(graph, graph.Create(Substance, body.Id, Attributes("Alpha")));
        var first = graph.Create("node-extension", spec.Id, Empty);
        var second = graph.Create("node-extension", spec.Id, Empty);
        Assert.NotEqual(first.IdentityKey, second.IdentityKey);
        var original = new SequenceNode(graph, "0000", first.Id, Empty, "Study 1");
        var renamed = new SequenceNode(graph, "0001", first.Id, Empty, "Study 1 updated");
        Assert.Equal(original.NodeInstanceId, renamed.NodeInstanceId);
        Assert.Equal("m3.2.s.4.1", renamed.CtdSection);
        Assert.Equal(NodeMetadataStatus.Complete, renamed.MetadataStatus);
        Assert.Equal(NodeMetadataStatus.NeedsMetadataCompletion, new SequenceNode(graph, "0001", second.Id, Empty).MetadataStatus);
    }

    [Fact]
    public void PlacementBindingPreservesLeafIdAndRequiresMatchingApplicationAndSequence()
    {
        var (graph, body) = CreateGraph();
        var spec = SpecificationNode(graph, graph.Create(Substance, body.Id, Attributes("Alpha")));
        var node = new SequenceNode(graph, "0000", spec.Id, Empty);
        var placement = new DocumentPlacement(Guid.NewGuid(), graph.ApplicationId, "0000", "m2.5",
            DocumentPlacementOperation.New, "Document", "imported-leaf-ID");
        placement.BindToNode(node, 12);
        placement.ReviseTitle("Revised title");
        Assert.Equal("m3.2.s.4.1", placement.CtdSection);
        Assert.Equal(spec.Id, placement.NodeInstanceId);
        Assert.Equal("imported-leaf-ID", placement.LeafId);
        Assert.Equal(12, placement.SortOrder);
        AssertCode("NodeSelectionRequired", () => placement.ReassignSection("m2.5"));
        AssertCode("PlacementNodeScopeMismatch", () => placement.BindToNode(new SequenceNode(graph, "0001", spec.Id, Empty)));
        var foreign = new DocumentPlacement(Guid.NewGuid(), Guid.NewGuid(), "0000", "m2.5", DocumentPlacementOperation.New, null);
        AssertCode("PlacementNodeScopeMismatch", () => foreign.BindToNode(node));
        var restored = DocumentPlacement.Rehydrate(placement.Id, placement.DocumentId, placement.ApplicationId,
            placement.SequenceNumber, placement.CtdSection, placement.Operation, placement.Title, null, placement.CreatedUtc,
            placement.LeafId, placement.NodeInstanceId, placement.SortOrder);
        Assert.Equal(placement.LeafId, restored.LeafId);
        Assert.Equal(placement.NodeInstanceId, restored.NodeInstanceId);
        Assert.Equal(placement.SortOrder, restored.SortOrder);
    }

    private static (CtdNodeGraph Graph, CtdNodeInstance Body) CreateGraph()
    {
        var graph = new CtdNodeGraph(Guid.NewGuid(), IchSectionDefinitions.Current);
        var quality = graph.Create(Quality, null, Empty);
        return (graph, graph.Create(Body, quality.Id, Empty));
    }

    private static CtdNodeInstance SpecificationNode(CtdNodeGraph graph, CtdNodeInstance substance)
    {
        var control = graph.Create(Control, substance.Id, Empty);
        return graph.Create(Specification, control.Id, Empty);
    }

    private static Dictionary<string, string> Attributes(string manufacturer) => new()
    {
        ["substance"] = "Drug A", ["manufacturer"] = manufacturer
    };

    private static void AssertCode(string code, Action action) => Assert.Equal(code, Assert.Throws<CtdNodeConstraintException>(action).Code);
}
