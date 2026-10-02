using System.Text.Json;
using System.Xml.Linq;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using static RATools.Tests.Applications.NodeImportWorkspace;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportNodeTests
{
    [Fact]
    public async Task IndependentFixturePreservesBusinessGroupsAddressesAttributesAndLifecycle()
    {
        using var scope = new NodeImportWorkspace();
        scope.CopyPublisherFixture();
        var result = await scope.ImportAsync();
        Assert.Equal(4, result.ImportedSequenceCount);
        Assert.Equal(13, result.ImportedDocumentCount);
        Assert.Equal(14, result.ImportedPlacementCount);
        Assert.All(result.Issues, issue => Assert.Equal("NODE_SCHEMA_NOT_AVAILABLE", issue.Code));
        await AssertFixtureAsync(scope, result.ApplicationId);
    }

    internal static async Task AssertFixtureAsync(NodeImportWorkspace scope, Guid applicationId)
    {
        var placements = (await scope.Placements.ListByApplicationAsync(applicationId)).ToDictionary(placement =>
            $"{placement.SequenceNumber}/{placement.ImportedSource!.BackboneRelativePath}#{placement.LeafId}");
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Publisher", "expected.json")));
        foreach (var group in expected.RootElement.GetProperty("nodeGroups").EnumerateArray())
        {
            var leaves = group.GetProperty("leaves").EnumerateArray().Select(item => placements[item.GetString()!]).ToArray();
            Assert.Single(leaves.Select(leaf => leaf.NodeInstanceId).Distinct());
            foreach (var leaf in leaves)
            {
                Assert.Equal(group.GetProperty("section").GetString(), leaf.CtdSection);
                if (leaf.NodeInstanceId is not { } nodeId) { Assert.StartsWith("m1.", leaf.CtdSection); continue; }
                var workspace = (await scope.Nodes.GetSequenceAsync(applicationId, leaf.SequenceNumber))!;
                var chain = new List<SequenceNode>();
                for (Guid? id = nodeId; id is not null; id = workspace.Graph.Get(id.Value).ParentInstanceId)
                    chain.Add(workspace.Nodes.Single(node => node.NodeInstanceId == id));
                foreach (var ancestor in group.GetProperty("ancestors").EnumerateArray())
                {
                    var node = Assert.Single(chain, item => workspace.Graph.Get(item.NodeInstanceId).DefinitionKey == ancestor.GetProperty("element").GetString());
                    foreach (var attribute in ancestor.GetProperty("attributes").EnumerateObject())
                        Assert.Equal(attribute.Value.GetString(), node.Attributes[attribute.Name]);
                    if (ancestor.TryGetProperty("title", out var title)) Assert.Equal(title.GetString(), node.Title);
                }
            }
        }
        foreach (var item in expected.RootElement.GetProperty("events").EnumerateArray())
        {
            var leaf = placements[item.GetProperty("leaf").GetString()!];
            var target = placements[item.GetProperty("target").GetString()!];
            Assert.Equal(target.Id, leaf.LifecycleTargetPlacementId);
            Assert.Equal(target.NodeInstanceId, leaf.NodeInstanceId);
            if (leaf.Operation == DocumentPlacementOperation.Delete)
            {
                Assert.Equal(target.DocumentId, leaf.DocumentId);
                Assert.Null(leaf.ImportedSource!.Href);
            }
        }
        Assert.NotEqual(placements["0000/index.xml#alpha-spec-v1"].NodeInstanceId, placements["0000/index.xml#beta-spec-v1"].NodeInstanceId);
        var original = (await scope.Nodes.GetSequenceAsync(applicationId, "0000"))!;
        var substances = original.Nodes.Where(node => node.CtdSection == "m3.2.s").OrderBy(node => node.SortOrder).ToArray();
        Assert.Equal(["Alpha", "Beta"], substances.Select(node => node.Attributes["manufacturer"]));
        var sources = await scope.Store.GetBackbonesAsync(applicationId, "0000");
        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, source => source.RelativePath == "m1/us/us-regional.xml" && source.Xml.Contains("<admin>"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualAttributeSiblingsRemainSeparateEvenAfterAnEarlierUniqueNode(bool earlierUnique)
    {
        using var scope = new NodeImportWorkspace();
        if (earlierUnique) await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("old"))));
        await scope.WriteAsync("0001", Quality(Substance("Alpha", Leaf("one", "one/specification.pdf")), Substance("Alpha", Leaf("two", "two/specification.pdf"))));
        await scope.WriteAsync("0002", Quality(Substance("Alpha", Leaf("three"))));
        var result = await scope.ImportAsync();
        Assert.Contains(result.Issues, issue => issue.Code == "NODE_IDENTITY_AMBIGUOUS");
        var nodes = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0001"))!;
        var placements = await scope.Placements.ListByApplicationAsync(result.ApplicationId);
        Assert.Equal(placements.Count, placements.Select(leaf => leaf.NodeInstanceId).Distinct().Count());
        Assert.All(nodes.Graph.Nodes.Values.Where(node => node.DefinitionKey == "m3-2-s-drug-substance"), node => Assert.Equal(CtdIdentityStatus.Ambiguous, node.IdentityStatus));
        Assert.All(nodes.Nodes.Where(node => node.CtdSection.StartsWith("m3.2.s", StringComparison.Ordinal)), node => Assert.Equal(NodeMetadataStatus.LegacyUnresolved, node.MetadataStatus));
        if (earlierUnique)
        {
            var previous = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
            Assert.Equal(NodeMetadataStatus.LegacyUnresolved, previous.Nodes.Single(node => node.CtdSection == "m3.2.s").MetadataStatus);
        }
    }

    [Fact]
    public async Task MissingManufacturerIsVisibleAndIsNotInferred()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Quality(Substance(null, Leaf("missing", "alpha/specification.pdf"))));
        var result = await scope.ImportAsync();
        Assert.Contains(result.Issues, issue => issue.Code == "NODE_METADATA_INCOMPLETE");
        var nodes = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        var substance = nodes.Nodes.Single(node => node.CtdSection == "m3.2.s");
        Assert.False(substance.Attributes.ContainsKey("manufacturer"));
        Assert.Equal(NodeMetadataStatus.NeedsMetadataCompletion, substance.MetadataStatus);
    }

    [Fact]
    public async Task ExtensionsUseExplicitHistoricalEvidenceAndRetainIndependentTitlesAndOrdering()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Studies(Extension("a", "Same title", Leaf("first", "one.pdf")), Extension("b", "Same title", Leaf("second", "two.pdf"))));
        await scope.WriteAsync("0001", Studies(Extension("changed-xml-id", "Revised title", Leaf("replacement", operation: "replace", target: "../0000/index.xml#second")),
            Extension("c", "Same title", Leaf("third", "three.pdf"))));
        var result = await scope.ImportAsync();
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == "Error");
        var leaves = (await scope.Placements.ListAsync()).ToDictionary(leaf => leaf.LeafId);
        Assert.Equal(leaves["second"].NodeInstanceId, leaves["replacement"].NodeInstanceId);
        Assert.Equal(3, leaves.Values.Select(leaf => leaf.NodeInstanceId).Distinct().Count());
        var current = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0001"))!;
        var previous = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        Assert.Equal("Revised title", current.Nodes.Single(node => node.NodeInstanceId == leaves["second"].NodeInstanceId).Title);
        Assert.Equal("Same title", previous.Nodes.Single(node => node.NodeInstanceId == leaves["second"].NodeInstanceId).Title);
        Assert.Equal(1, previous.Nodes.Single(node => node.NodeInstanceId == leaves["second"].NodeInstanceId).SortOrder);
    }

    [Theory]
    [InlineData("attribute")]
    [InlineData("file")]
    [InlineData("history")]
    public async Task FailedSequenceDoesNotPublishNodesDocumentsOrHistoryEntries(string failure)
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("old"))));
        var invalid = Substance("Beta", Leaf("failed", "failed.pdf"));
        if (failure == "attribute") invalid.SetAttributeValue("invented", "forbidden");
        if (failure == "history") invalid.Descendants("leaf").Single().SetAttributeValue("operation", "replace");
        if (failure == "history") invalid.Descendants("leaf").Single().SetAttributeValue("modified-file", "../0000/index.xml#old");
        await scope.WriteAsync("0001", Quality(invalid));
        if (failure == "file") File.Delete(Path.Combine(scope.Root, "0001", "failed.pdf"));
        await scope.WriteAsync("0002", Quality(Substance("Beta", Leaf("must-not-resolve", operation: "delete", target: "../0001/index.xml#failed"))));
        var result = await scope.ImportAsync();
        Assert.Equal(1, result.ImportedSequenceCount);
        Assert.Equal(2, result.FailedSequenceCount);
        Assert.Equal("old", Assert.Single(await scope.Placements.ListAsync()).LeafId);
        Assert.Single(await scope.Documents.ListAsync());
        Assert.Null(await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0001"));
        Assert.Empty(await scope.Store.GetBackbonesAsync(result.ApplicationId, "0001"));
        Assert.DoesNotContain(scope.Batch!.Graph.Nodes.Values, node => node.IdentityAttributes.GetValueOrDefault("manufacturer") == "Beta");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryImportFailureOrCancellationLeavesNoApplicationOrNodeState(bool cancel)
    {
        using var scope = new NodeImportWorkspace();
        using var cancellation = new CancellationTokenSource();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("first"))));
        scope.Placements.FailAfterAdd = !cancel;
        scope.Placements.CancelAfterAdd = cancel ? cancellation : null;
        var failure = await Record.ExceptionAsync(() => scope.ImportAsync(cancellation.Token));
        Assert.NotNull(failure);
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(failure); else Assert.IsType<IOException>(failure);
        Assert.Empty(await scope.Applications.ListAsync());
        Assert.Empty(await scope.Documents.ListAsync());
        Assert.Empty(await scope.Placements.ListAsync());
        Assert.Empty(await scope.Store.GetBackbonesAsync(scope.Batch!.Application.Id, "0000"));
        await scope.Applications.AddAsync(scope.Batch.Application);
        var workspace = (await scope.Nodes.GetSequenceAsync(scope.Batch.Application.Id, "0000"))!;
        Assert.Empty(workspace.Graph.Nodes);
        Assert.True(File.Exists(Path.Combine(scope.Root, "0000", "specification.pdf")));
    }

    [Fact]
    public async Task LegacyBareHrefIsMatchedOnlyWithinTheUniqueBusinessNode()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("alpha")), Substance("Beta", Leaf("beta"))));
        await scope.WriteAsync("0001", Quality(Substance("Alpha", Leaf("replacement", operation: "replace", target: "specification.pdf"))));
        var result = await scope.ImportAsync();
        Assert.Equal(2, result.ImportedSequenceCount);
        var leaves = (await scope.Placements.ListAsync()).ToDictionary(leaf => leaf.LeafId);
        Assert.Equal(leaves["alpha"].Id, leaves["replacement"].LifecycleTargetPlacementId);
        Assert.NotEqual(leaves["beta"].Id, leaves["replacement"].LifecycleTargetPlacementId);
    }

    [Fact]
    public async Task FailedSequenceCannotMakeAnEarlierNodeAmbiguous()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("old"))));
        await scope.WriteAsync("0001", Quality(Substance("Alpha", Leaf("one", "one.pdf")), Substance("Alpha", Leaf("two", "two.pdf"))));
        File.Delete(Path.Combine(scope.Root, "0001", "two.pdf"));
        await scope.WriteAsync("0002", Quality(Substance("Alpha", Leaf("current", operation: "replace", target: "../0000/index.xml#old"))));
        var result = await scope.ImportAsync();
        Assert.Equal(2, result.ImportedSequenceCount);
        Assert.Equal(1, result.FailedSequenceCount);
        var workspace = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        Assert.All(workspace.Graph.Nodes.Values, node => Assert.Equal(CtdIdentityStatus.Resolved, node.IdentityStatus));
        var leaves = (await scope.Placements.ListAsync()).ToDictionary(leaf => leaf.LeafId);
        Assert.Equal(leaves["old"].NodeInstanceId, leaves["current"].NodeInstanceId);
    }

    [Fact]
    public async Task EncodedHrefAndLeafOrderArePreservedWithoutMovingFiles()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("one", "s%70ecification.pdf"), Leaf("two", "./second.pdf"))));
        var result = await scope.ImportAsync();
        Assert.Equal(1, result.ImportedSequenceCount);
        var leaves = (await scope.Placements.ListAsync()).OrderBy(leaf => leaf.SortOrder).ToArray();
        Assert.Equal([0, 1], leaves.Select(leaf => leaf.SortOrder));
        Assert.Equal(["s%70ecification.pdf", "./second.pdf"], leaves.Select(leaf => leaf.ImportedSource!.Href));
        Assert.True(File.Exists(Path.Combine(scope.Root, "0000", "specification.pdf")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(scope.Root, "0000"), "*.pdf").Length);
    }

    [Fact]
    public async Task NestedExtensionsReuseOnlyTheExplicitlyReferencedAncestorChain()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", Studies(Extension("outer", "Outer", Extension("inner", "Inner", Leaf("old")))));
        await scope.WriteAsync("0001", Studies(Extension("outer2", "Changed", Extension("inner2", "Changed too",
            Leaf("current", operation: "replace", target: "../0000/index.xml#old")))));
        var result = await scope.ImportAsync();
        Assert.Equal(2, result.ImportedSequenceCount);
        var previous = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        var current = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0001"))!;
        Assert.Equal(previous.Nodes.Select(node => node.NodeInstanceId).Order(), current.Nodes.Select(node => node.NodeInstanceId).Order());
    }

    [Fact]
    public async Task ACustomRegionalBackboneRetainsItsAddressAndRawRegionalAttributes()
    {
        using var scope = new NodeImportWorkspace();
        foreach (var number in new[] { "0000", "0001" })
        {
            var root = Path.Combine(scope.Root, number, "m1", "us");
            var fileName = number == "0000" ? "source.xml" : "current.xml";
            await scope.WriteAsync(number, new XElement("m1-administrative-information-and-prescribing-information",
                Leaf("regional-" + number, "m1/us/" + fileName)));
            var regional = new XDocument(new XElement("fda-regional", new XElement("m1-2-cover-letters",
                new XAttribute("language", "en"), new XAttribute("country", "US"),
                number == "0000" ? Leaf("regional-leaf", "cover.pdf") : Leaf("regional-delete", operation: "delete", target: "../../../0000/m1/us/source.xml#regional-leaf"))));
            regional.Save(Path.Combine(root, fileName));
            if (number == "0000") await File.WriteAllTextAsync(Path.Combine(root, "cover.pdf"), "regional document");
        }
        var result = await scope.ImportAsync();
        Assert.Equal(2, result.ImportedSequenceCount);
        Assert.Equal(1, result.ImportedDocumentCount);
        var leaves = (await scope.Placements.ListAsync()).ToDictionary(leaf => leaf.LeafId);
        Assert.Equal(leaves["regional-leaf"].Id, leaves["regional-delete"].LifecycleTargetPlacementId);
        Assert.Equal("m1/us/source.xml", leaves["regional-leaf"].ImportedSource!.BackboneRelativePath);
        Assert.Equal("m1/us/current.xml", leaves["regional-delete"].ImportedSource!.BackboneRelativePath);
        Assert.Null(leaves["regional-leaf"].NodeInstanceId);
        var source = Assert.Single(await scope.Store.GetBackbonesAsync(result.ApplicationId, "0000"), source => source.RelativePath == "m1/us/source.xml");
        Assert.Equal("en", XDocument.Parse(source.Xml).Descendants("m1-2-cover-letters").Single().Attribute("language")!.Value);
        var nodes = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        Assert.Equal("NODE_SCHEMA_NOT_AVAILABLE", Assert.Single(nodes.Diagnostics).Code);
    }

    [Fact]
    public async Task TheIchIndexCannotAlsoBeItsOwnRegionalBackbone()
    {
        using var scope = new NodeImportWorkspace();
        await scope.WriteAsync("0000", new XElement("m1-administrative-information-and-prescribing-information", Leaf("self", "index.xml")));
        var result = await scope.ImportAsync();
        Assert.Equal(1, result.FailedSequenceCount);
        Assert.Equal(0, result.ImportedSequenceCount);
        Assert.Contains(result.Issues, issue => issue.Code == "SEQUENCE_INDEX_INVALID");
        Assert.Empty(await scope.Placements.ListAsync());
        Assert.Empty(scope.Batch!.Graph.Nodes);
    }
}
