using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Tests.Applications;

public sealed class LegacyNodeBackfillPlanTests
{
    [Fact]
    public void RepeatedLegacyBranchesStaySeparateAndDoNotInventAttributes()
    {
        var graph = new CtdNodeGraph(Guid.NewGuid(), IchSectionDefinitions.Current);
        var workspace = new CtdSequenceWorkspace(graph, "0000", 0, [], []);
        LegacyPlacementNodeInput[] placements = [new(Guid.NewGuid(), "m3.2.s.4.1", null), new(Guid.NewGuid(), "m3.2.s.4.1", null)];
        var plan = LegacyNodeBackfillPlan.Create(workspace, placements);
        var repeated = plan.Graph.Nodes.Values.Where(node => node.DefinitionKey == "m3-2-s-drug-substance").ToArray();
        Assert.Equal(2, repeated.Length);
        Assert.All(repeated, node =>
        {
            Assert.Empty(node.IdentityAttributes);
            Assert.Equal(CtdIdentityStatus.Ambiguous, node.IdentityStatus);
        });
        Assert.Equal(2, plan.Bindings.Values.Distinct().Count());
        Assert.Equal(2, plan.Diagnostics.Count);
        Assert.Empty(graph.Nodes);
        var repeatedPreview = LegacyNodeBackfillPlan.Create(workspace, placements.Reverse().ToArray());
        Assert.Equal(plan.Graph.Nodes.Keys.Order(), repeatedPreview.Graph.Nodes.Keys.Order());
        var applied = new CtdSequenceWorkspace(plan.Graph, "0000", 1, plan.Nodes, plan.Diagnostics);
        var again = LegacyNodeBackfillPlan.Create(applied, placements.Select(item => item with { NodeInstanceId = plan.Bindings[item.PlacementId] }).ToArray());
        Assert.Empty(again.Bindings);
        Assert.Equal(plan.Graph.Nodes.Keys.Order(), again.Graph.Nodes.Keys.Order());
    }

    [Fact]
    public void PlainSectionsShareDeterministicNodesButAmbiguousAndRegionalMappingsRemainUnbound()
    {
        var graph = new CtdNodeGraph(Guid.NewGuid(), IchSectionDefinitions.Current);
        var workspace = new CtdSequenceWorkspace(graph, "0000", 0, [], []);
        LegacyPlacementNodeInput[] placements =
        [
            new(Guid.NewGuid(), "m2.5", null), new(Guid.NewGuid(), "m2.5", null),
            new(Guid.NewGuid(), "m2.3", null), new(Guid.NewGuid(), "m1.2", null)
        ];
        var plan = LegacyNodeBackfillPlan.Create(workspace, placements);
        Assert.Equal(plan.Bindings[placements[0].PlacementId], plan.Bindings[placements[1].PlacementId]);
        Assert.Contains(plan.Diagnostics, issue => issue.PlacementId == placements[2].PlacementId && issue.Code == "AmbiguousSectionDefinition");
        Assert.Contains(plan.Diagnostics, issue => issue.PlacementId == placements[3].PlacementId && issue.Code == "SectionDefinitionUnavailable");
        Assert.Equal(2, plan.Bindings.Count);
        Assert.All(plan.Nodes, node => Assert.Equal(NodeMetadataStatus.Complete, node.MetadataStatus));
    }

    [Fact]
    public void InvalidLegacySequenceIsDiagnosedWithoutCreatingNodes()
    {
        var workspace = new CtdSequenceWorkspace(new CtdNodeGraph(Guid.NewGuid(), IchSectionDefinitions.Current), "draft", 0, [], []);
        var plan = LegacyNodeBackfillPlan.Create(workspace, [new(Guid.NewGuid(), "m2.5", null)]);
        Assert.Empty(plan.Nodes);
        Assert.Empty(plan.Bindings);
        Assert.Equal("InvalidSequenceNumber", Assert.Single(plan.Diagnostics).Code);
    }
}
