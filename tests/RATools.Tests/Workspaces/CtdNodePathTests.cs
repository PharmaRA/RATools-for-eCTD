using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.Validation;
using RATools.Domain.Ctd;
using System.Xml.Linq;

namespace RATools.Tests.Workspaces;

[Trait("Category", "PathSecurity")]
public sealed class CtdNodePathTests
{
    [Fact]
    public void PinnedPathsCoverEveryIchDefinitionAndMatchIndependentFixtureHrefs()
    {
        var definitions = IchSectionDefinitions.Current;
        Assert.Equal(158, IchNodePathProfile.Paths.Count);
        Assert.All(definitions.Definitions.Values.Where(item => item.SectionPath is not null && item.SectionPath != "m1"),
            item => Assert.True(IchNodePathProfile.Paths.ContainsKey(item.DefinitionKey), item.DefinitionKey));
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Publisher", "sequences", "0000", "index.xml"));
        var graph = new CtdNodeGraph(Guid.NewGuid(), definitions);
        var nodes = new List<SequenceNode>();
        var leaves = new List<(Guid NodeId, string Href)>();
        foreach (var module in xml.Root!.Elements().Where(element => !element.Name.LocalName.StartsWith("m1", StringComparison.Ordinal))) Visit(module, null);
        var workspace = new CtdSequenceWorkspace(graph, "0000", 0, nodes, []);
        var resolver = new CtdNodePathResolver(new EctdWorkspacePathResolver());
        resolver.ValidateAllocation("us-fda-ectd-3.2.2", workspace);
        Assert.Equal(9, leaves.Count);
        foreach (var (node, href) in leaves)
            Assert.Equal(href, resolver.ResolveFile("us-fda-ectd-3.2.2", workspace, node, Path.GetFileName(href)));

        void Visit(XElement element, Guid? parent)
        {
            var attributes = element.Attributes().ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value);
            var instance = graph.Create(element.Name.LocalName, parent, attributes);
            var definition = definitions.Get(instance.DefinitionKey);
            var segment = definition.Kind == CtdNodeKind.Extension ? attributes["ID"] : definition.Repeatable
                ? attributes.ContainsKey("substance") ? "drug-a-" + attributes["manufacturer"].ToLowerInvariant()
                    : attributes.ContainsKey("product-name") ? "tablet-alpha" : "indication-a"
                : null;
            nodes.Add(new SequenceNode(graph, "0000", instance.Id, attributes, element.Element("title")?.Value, storageSegment: segment));
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "leaf") leaves.Add((instance.Id, child.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))!.Value));
                else if (child.Name.LocalName != "title") Visit(child, instance.Id);
            }
        }
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2")]
    [InlineData("eu-ectd-3.2.2")]
    public void ManufacturersHaveDistinctStablePaths(string template)
    {
        var workspace = Create();
        var resolver = new CtdNodePathResolver(new EctdWorkspacePathResolver());
        resolver.ValidateAllocation(template, workspace);
        var leaves = workspace.Nodes.Where(node => node.CtdSection == "m3.2.s.4.1").ToArray();
        var paths = leaves.Select(node => resolver.ResolveFile(template, workspace, node.NodeInstanceId, "specification.pdf")).ToArray();
        Assert.Equal(2, paths.Distinct().Count());
        Assert.Contains("/drug-a-alpha/", paths[0]);
        Assert.Contains("/drug-a-beta/", paths[1]);
        var retitled = workspace with { Nodes = workspace.Nodes.Select(node => new SequenceNode(workspace.Graph, "0000",
            node.NodeInstanceId, node.Attributes, "New title", node.SortOrder + 1, node.StorageSegment)).ToArray() };
        Assert.Equal(paths[0], resolver.ResolveFile(template, retitled, leaves[0].NodeInstanceId, "specification.pdf"));
    }

    [Fact]
    public void DuplicateInstanceDirectoryIsRejected()
    {
        var workspace = Create("same", "same");
        Assert.Throws<CtdNodeConstraintException>(() => new CtdNodePathResolver(new EctdWorkspacePathResolver())
            .ValidateAllocation("us-fda-ectd-3.2.2", workspace));
    }

    [Theory]
    [InlineData("../other.pdf")]
    [InlineData("CON.pdf")]
    [InlineData("file.pdf:stream")]
    [InlineData("file\\other.pdf")]
    [InlineData("other.pdf.")]
    [InlineData("con.pdf")]
    [InlineData("lpt1.pdf")]
    [InlineData("file..pdf")]
    [InlineData(".pdf")]
    [InlineData("file--name.pdf")]
    [InlineData("file name.pdf")]
    [InlineData("文件.pdf")]
    public void UnsafeFileNamesAreRejected(string name)
    {
        var workspace = Create();
        var node = workspace.Nodes.First(item => item.CtdSection == "m3.2.s.4.1");
        Assert.ThrowsAny<Exception>(() => new CtdNodePathResolver(new EctdWorkspacePathResolver())
            .ResolveFile("us-fda-ectd-3.2.2", workspace, node.NodeInstanceId, name));
    }

    [Fact]
    public void FileLengthBoundaryIsEnforced()
    {
        var workspace = Create();
        var node = workspace.Nodes.First(item => item.CtdSection == "m3.2.s.4.1");
        var resolver = new CtdNodePathResolver(new EctdWorkspacePathResolver());
        Assert.EndsWith(new string('a', 60) + ".pdf", resolver.ResolveFile("us-fda-ectd-3.2.2", workspace, node.NodeInstanceId, new string('a', 60) + ".pdf"));
        Assert.Equal("NodeFileNameInvalid", Assert.Throws<CtdNodeConstraintException>(() =>
            resolver.ResolveFile("us-fda-ectd-3.2.2", workspace, node.NodeInstanceId, new string('a', 61) + ".pdf")).Code);
    }

    [Theory]
    [InlineData("Alpha")]
    [InlineData("alpha_beta")]
    [InlineData("-alpha")]
    [InlineData("alpha--beta")]
    public void InvalidInstanceSegmentsAreRejected(string segment)
    {
        var workspace = Create(segment);
        Assert.Throws<CtdNodeConstraintException>(() => new CtdNodePathResolver(new EctdWorkspacePathResolver())
            .ValidateAllocation("us-fda-ectd-3.2.2", workspace));
    }

    [Fact]
    public void MissingAncestorAndOverlongNestedExtensionPathsAreRejected()
    {
        var workspace = Create();
        var leaf = workspace.Nodes.First(item => item.CtdSection == "m3.2.s.4.1");
        var resolver = new CtdNodePathResolver(new EctdWorkspacePathResolver());
        Assert.Equal("SequenceNodeParentMissing", Assert.Throws<CtdNodeConstraintException>(() => resolver.Resolve(
            "us-fda-ectd-3.2.2", workspace with { Nodes = [leaf] }, leaf.NodeInstanceId)).Code);
        var parent = leaf.NodeInstanceId;
        var nodes = workspace.Nodes.ToList();
        for (var index = 0; index < 4; index++)
        {
            var extension = workspace.Graph.Create("node-extension", parent, new Dictionary<string, string>());
            nodes.Add(new SequenceNode(workspace.Graph, "0000", extension.Id, extension.IdentityAttributes, "Study", storageSegment: new string('a', 64)));
            parent = extension.Id;
        }
        Assert.Equal("NodePathTooLong", Assert.Throws<CtdNodeConstraintException>(() => resolver.ResolveFile(
            "us-fda-ectd-3.2.2", workspace with { Nodes = nodes }, parent, "report.pdf")).Code);
    }

    internal static CtdSequenceWorkspace Create(string alpha = "drug-a-alpha", string beta = "drug-a-beta", Guid? applicationId = null)
    {
        var graph = new CtdNodeGraph(applicationId ?? Guid.NewGuid(), IchSectionDefinitions.Current);
        var root = graph.Create("m3-quality", null, new Dictionary<string, string>());
        var body = graph.Create("m3-2-body-of-data", root.Id, new Dictionary<string, string>());
        var segments = new Dictionary<Guid, string>();
        foreach (var (manufacturer, segment) in new[] { ("Alpha", alpha), ("Beta", beta) })
        {
            var substance = graph.Create("m3-2-s-drug-substance", body.Id,
                new Dictionary<string, string> { ["substance"] = "Drug A", ["manufacturer"] = manufacturer });
            segments[substance.Id] = segment;
            var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
            graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>());
        }
        return new CtdSequenceWorkspace(graph, "0000", 0,
            graph.Nodes.Values.Select(node => new SequenceNode(graph, "0000", node.Id, node.IdentityAttributes,
                storageSegment: segments.GetValueOrDefault(node.Id))).ToArray(), []);
    }
}
