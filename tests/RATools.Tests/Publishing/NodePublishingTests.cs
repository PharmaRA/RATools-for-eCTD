using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Documents;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;
using RATools.Domain.Ctd;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;
using RATools.Tests.Applications;
using RATools.Tests.TestDoubles;
using static RATools.Tests.Applications.NodeImportWorkspace;

namespace RATools.Tests.Publishing;

public sealed class NodePublishingTests
{
    [Fact]
    public async Task FourSequenceDeliveryPreservesTheIndependentBusinessOracleAndReimports()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        using var restored = new NodeImportWorkspace();
        await PublishFixtureAsync(source, restored.Root, imported.ApplicationId);
        var result = await restored.ImportAsync();
        Assert.Equal(4, result.ImportedSequenceCount);
        Assert.Equal(13, result.ImportedDocumentCount);
        Assert.Equal(14, result.ImportedPlacementCount);
        await ApplicationImportNodeTests.AssertFixtureAsync(restored, result.ApplicationId);
    }

    internal static EctdPackageModelBuilder Builder(NodeImportWorkspace source) => new(source.Applications, source.Placements,
        source.Documents, new FdaEctd322StandardsProfileProvider(), new DocumentStorageBoundary(new ConfiguredWorkspacePathPolicy(
            Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [source.Root] }))), source.Nodes, source.Revisions, source.Store);

    internal static async Task<IReadOnlyDictionary<string, GeneratedBackboneDto>> PublishFixtureAsync(NodeImportWorkspace source, string restoredRoot, Guid applicationId)
    {
        var deliveries = new Dictionary<string, GeneratedBackboneDto>(StringComparer.Ordinal);
        var profiles = new FdaEctd322StandardsProfileProvider();
        var builder = Builder(source);
        var service = new BackboneService(builder, new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]),
            new EctdXmlValidator(), profiles, new LocalBackboneFileWriter(
                Options.Create(new BackboneOutputOptions { RootPath = Path.Combine(source.Root, "delivery-output"), RetainJobRuns = 0 }),
                NullLogger<LocalBackboneFileWriter>.Instance));
        var application = (await source.Applications.GetAsync(applicationId))!;
        foreach (var sequence in application.Sequences.OrderBy(sequence => sequence.SequenceNumber, StringComparer.Ordinal))
        {
            var number = sequence.SequenceNumber;
            var package = await builder.BuildAsync(new(applicationId, number));
            Assert.Equal(1, package.WorkspaceRevision);
            var output = await service.GenerateAsync(new GenerateBackboneRequest(applicationId, number, Guid.NewGuid(), "report.json", "package.zip"));
            deliveries.Add(number, output);
            var directory = Path.GetDirectoryName(output.FilePath)!;
            var destination = Path.Combine(restoredRoot, number);
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var copied = Path.Combine(destination, Path.GetRelativePath(directory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                File.Copy(file, copied);
            }
            using var zip = ZipFile.OpenRead(output.PackagePath);
            foreach (var file in package.PublishedFiles)
            {
                Assert.NotNull(zip.GetEntry(file.Href));
                Assert.Equal(await File.ReadAllBytesAsync(file.SourcePath), await File.ReadAllBytesAsync(Path.Combine(destination, file.Href)));
            }
            Assert.Equal(package.PublishedFiles.Count, Directory.GetFiles(destination, "*.pdf", SearchOption.AllDirectories).Length);
            // The report is a separate job artifact written by PublishJobService.
            Directory.CreateDirectory(Path.GetDirectoryName(output.ReportPath)!);
            await File.WriteAllTextAsync(output.ReportPath, "{}");
            var integrity = await new PublishOutputVerifier().VerifyAsync(output.FilePath, output.ReportPath, output.PackagePath);
            Assert.DoesNotContain(integrity.Evidence.Findings, finding => finding.Severity == "Error" || finding.Type == "OrphanFile");
            var index = EctdWorkspaceFixture.ReadXml(output.FilePath);
            var original = EctdWorkspaceFixture.ReadXml(Path.Combine(source.Root, number, "index.xml"));
            Assert.Equal(original.Descendants("leaf").Select(leaf => leaf.Attribute("ID")!.Value).Order(),
                index.Descendants("leaf").Select(leaf => leaf.Attribute("ID")!.Value).Order());
        }
        await AssertHistoricalAddressesAsync(restoredRoot);
        return deliveries;
    }

    private static async Task AssertHistoricalAddressesAsync(string root)
    {
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Publisher", "expected.json")));
        foreach (var item in expected.RootElement.GetProperty("events").EnumerateArray())
        {
            var address = item.GetProperty("leaf").GetString()!.Split('#');
            var sourcePath = Path.Combine(root, address[0]);
            var source = EctdWorkspaceFixture.ReadXml(sourcePath).Descendants("leaf").Single(leaf => leaf.Attribute("ID")?.Value == address[1]);
            var uri = new Uri(new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = sourcePath }.Uri, source.Attribute("modified-file")!.Value);
            Assert.Equal(item.GetProperty("target").GetString(), Path.GetRelativePath(root, uri.LocalPath).Replace('\\', '/') + uri.Fragment);
            Assert.Single(EctdWorkspaceFixture.ReadXml(uri.LocalPath).Descendants("leaf"), leaf => leaf.Attribute("ID")?.Value == uri.Fragment[1..]);
        }
    }

    [Fact]
    public async Task NodeAndLeafSortOrderIsStableAndIndependentOfCollectionOrder()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        var package = await Builder(source).BuildAsync(new(imported.ApplicationId, "0000"));
        var writer = new IchIndexXmlWriter();
        Assert.Equal(writer.Write(package).XmlContent, writer.Write(package with
        {
            Nodes = package.Nodes!.Reverse().ToArray(), IchBackboneLeaves = package.IchBackboneLeaves.Reverse().ToArray()
        }).XmlContent);
        var revised = package with { Nodes = package.Nodes!.Select(node => node.DefinitionKey == "m3-2-s-drug-substance"
            ? node with { SortOrder = node.Attributes["manufacturer"] == "Beta" ? 0 : 1 } : node).ToArray() };
        var xml = writer.Write(revised).Document;
        Assert.Equal(["Beta", "Alpha"], xml.Descendants("m3-2-s-drug-substance").Select(node => node.Attribute("manufacturer")!.Value));
        Assert.Equal(writer.Write(revised).XmlContent, writer.Write(revised with { Nodes = revised.Nodes!.Reverse().ToArray() }).XmlContent);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    public async Task IncompleteBusinessIdentityBlocksPackageConstruction(string problem)
    {
        using var source = new NodeImportWorkspace();
        var first = Substance(problem == "missing" ? null : "Alpha", Leaf("one", "one.pdf"));
        await source.WriteAsync("0000", problem == "missing" ? Quality(first) : Quality(first, Substance("Alpha", Leaf("two", "two.pdf"))));
        var imported = await source.ImportAsync();
        var failure = await Assert.ThrowsAsync<EctdPackageNodeException>(() => Builder(source).BuildAsync(new(imported.ApplicationId, "0000")));
        Assert.Equal("NodeMetadataIncomplete", failure.Code);
        Assert.NotNull(failure.NodeInstanceId);
    }

    [Fact]
    public async Task MixedLeavesAndExtensionsUseSharedSortPositionsAndStableIds()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        var package = await Builder(source).BuildAsync(new(imported.ApplicationId, "0000"));
        var extension = package.Nodes!.Single(node => node.Attributes.GetValueOrDefault("ID") == "study-nc-001");
        var original = package.IchBackboneLeaves.Single(leaf => leaf.LeafId == "nc-report");
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        package = package with
        {
            Nodes = package.Nodes!.Select(node => node == extension ? node with { SortOrder = 1 } : node).ToArray(),
            IchBackboneLeaves = package.IchBackboneLeaves.Concat([
                original with { PlacementId = secondId, LeafId = "parent-second", NodeInstanceId = extension.ParentInstanceId, SortOrder = 2 },
                original with { PlacementId = firstId, LeafId = "parent-first", NodeInstanceId = extension.ParentInstanceId, SortOrder = 2 },
                original with { PlacementId = Guid.NewGuid(), LeafId = "parent-zero", NodeInstanceId = extension.ParentInstanceId, SortOrder = 0 }
            ]).ToArray()
        };
        var writer = new IchIndexXmlWriter();
        var output = writer.Write(package);
        Assert.Equal(["parent-zero", "study-nc-001", "parent-first", "parent-second"],
            output.Document.Descendants("m4-2-3-2-repeat-dose-toxicity").Single().Elements().Select(element => element.Attribute("ID")!.Value));
        Assert.Equal(output.XmlContent, writer.Write(package with
        {
            Nodes = package.Nodes!.Reverse().ToArray(), IchBackboneLeaves = package.IchBackboneLeaves.Reverse().ToArray()
        }).XmlContent);
        new EctdXmlValidator().Validate(new BackboneGeneratedFile("index.xml", output.XmlContent), new FdaEctd322StandardsProfileProvider().GetProfile("us-fda-ectd-3.2.2"));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("replace")]
    [InlineData("append")]
    public async Task RegionalBackboneReferenceMustAlwaysBeNew(string operation)
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        var package = await Builder(source).BuildAsync(new(imported.ApplicationId, "0000"));
        package = package with { RegionalBackbones = [new("m1/us/us-regional.xml", "regional-0000", "Invalid operation", operation, "../0000/index.xml#old", "checksum")] };
        Assert.Equal("RegionalReferenceMustBeNew", Assert.Throws<EctdPackageNodeException>(() => new IchIndexXmlWriter().Write(package)).Code);
    }

    [Theory]
    [InlineData("attribute", "NodeMetadataIncomplete")]
    [InlineData("version", "UnsupportedNodeSchema")]
    [InlineData("cycle", "NodeParentCycle")]
    [InlineData("duplicate-id", "DuplicateXmlId")]
    [InlineData("unbound", "NodeBindingRequired")]
    [InlineData("equal-identity", "AmbiguousNodeIdentity")]
    public async Task WriterRejectsAnInvalidPackageNodeTree(string defect, string code)
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        var package = await Builder(source).BuildAsync(new(imported.ApplicationId, "0000"));
        var selected = package.Nodes!.First(node => node.DefinitionKey == "m3-2-s-drug-substance");
        var attributes = selected.Attributes.ToDictionary();
        if (defect == "attribute") attributes.Add("invented", "not allowed");
        if (defect == "duplicate-id") attributes["ID"] = package.IchBackboneLeaves.First().LeafId;
        if (defect == "equal-identity") attributes["manufacturer"] = package.Nodes!.Single(node =>
            node.DefinitionKey == selected.DefinitionKey && node.NodeInstanceId != selected.NodeInstanceId).Attributes["manufacturer"];
        var invalid = selected with { Attributes = attributes,
            DefinitionVersion = defect == "version" ? "unsupported" : selected.DefinitionVersion,
            ParentInstanceId = defect == "cycle" ? selected.NodeInstanceId : selected.ParentInstanceId };
        package = package with { Nodes = package.Nodes!.Select(node => node.NodeInstanceId == selected.NodeInstanceId ? invalid : node).ToArray() };
        if (defect == "unbound") package = package with { IchBackboneLeaves = package.IchBackboneLeaves.Select(leaf => leaf with { NodeInstanceId = null }).ToArray() };
        Assert.Equal(code, Assert.Throws<EctdPackageNodeException>(() => new IchIndexXmlWriter().Write(package)).Code);
    }

    [Fact]
    public async Task EmptyIncompleteGroupsArePrunedAndXmlLangSurvivesImportAndOutput()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var path = Path.Combine(source.Root, "0000", "index.xml");
        var input = EctdWorkspaceFixture.ReadXml(path);
        input.Descendants("m3-2-s-drug-substance").First().SetAttributeValue(XNamespace.Xml + "lang", "en");
        input.Save(path);
        var imported = await source.ImportAsync();
        Assert.Equal(4, imported.ImportedSequenceCount);
        var package = await Builder(source).BuildAsync(new(imported.ApplicationId, "0000"));
        var parent = package.Nodes!.First(node => node.DefinitionKey == "m3-2-body-of-data");
        package = package with { Nodes = package.Nodes!.Append(new EctdPackageNode(Guid.NewGuid(), parent.NodeInstanceId,
            "m3-2-s-drug-substance", parent.DefinitionVersion, new Dictionary<string, string>(), null, 8, NodeMetadataStatus.NeedsMetadataCompletion)).ToArray() };
        var output = new IchIndexXmlWriter().Write(package);
        Assert.Equal(2, output.Document.Descendants("m3-2-s-drug-substance").Count());
        Assert.Equal("en", output.Document.Descendants("m3-2-s-drug-substance").First().Attribute(XNamespace.Xml + "lang")?.Value);
        new EctdXmlValidator().Validate(new BackboneGeneratedFile("index.xml", output.XmlContent), new FdaEctd322StandardsProfileProvider().GetProfile("us-fda-ectd-3.2.2"));
    }

    [Fact]
    public async Task CustomRegionalBackboneAndEncodedHrefRemainValidInTheDeliveredFiles()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var regionalPath = Path.Combine(source.Root, "0000", "m1", "us", "us-regional.xml");
        var regional = EctdWorkspaceFixture.ReadXml(regionalPath);
        var cover = regional.Descendants("leaf").Single();
        cover.Attributes().Single(attribute => attribute.Name.LocalName == "href").Value = "../12-cover-letters/c%6fver.pdf";
        var customPath = Path.Combine(source.Root, "0000", "m1", "us", "backbones", "source.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(customPath)!);
        regional.Save(customPath);
        File.Delete(regionalPath);
        var indexPath = Path.Combine(source.Root, "0000", "index.xml");
        var index = EctdWorkspaceFixture.ReadXml(indexPath);
        var reference = index.Descendants("leaf").Single(leaf => leaf.Attribute("ID")?.Value == "regional-0000");
        reference.SetAttributeValue("checksum", null);
        reference.Attributes().Single(attribute => attribute.Name.LocalName == "href").Value = "m1/us/backbones/source.xml";
        index.Save(indexPath);
        var currentRegionalPath = Path.Combine(source.Root, "0001", "m1", "us", "us-regional.xml");
        var currentRegional = EctdWorkspaceFixture.ReadXml(currentRegionalPath);
        var replacement = new XElement(cover);
        replacement.SetAttributeValue("ID", "cover-replacement");
        replacement.SetAttributeValue("operation", "replace");
        replacement.SetAttributeValue("checksum", null);
        replacement.SetAttributeValue("modified-file", "../../../0000/m1/us/backbones/source.xml#overview");
        replacement.Attributes().Single(attribute => attribute.Name.LocalName == "href").Value = "12-cover-letters/cover-new.pdf";
        currentRegional.Root!.Add(new XElement("m1-regional", new XElement("m1-2-cover-letters", replacement)));
        currentRegional.Save(currentRegionalPath);
        var replacementPath = Path.Combine(source.Root, "0001", "m1", "us", "12-cover-letters", "cover-new.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(replacementPath)!);
        File.Copy(Path.Combine(source.Root, "0000", "m1", "us", "12-cover-letters", "cover.pdf"), replacementPath);
        var currentIndexPath = Path.Combine(source.Root, "0001", "index.xml");
        var currentIndex = EctdWorkspaceFixture.ReadXml(currentIndexPath);
        currentIndex.Descendants("leaf").Single(leaf => leaf.Attribute("ID")?.Value == "regional-0001").SetAttributeValue("checksum", null);
        currentIndex.Save(currentIndexPath);
        var imported = await source.ImportAsync();
        Assert.Equal(4, imported.ImportedSequenceCount);
        using var restored = new NodeImportWorkspace();
        await PublishFixtureAsync(source, restored.Root, imported.ApplicationId);
        var deliveredPath = Path.Combine(restored.Root, "0000", "m1", "us", "backbones", "source.xml");
        var delivered = EctdWorkspaceFixture.ReadXml(deliveredPath);
        Assert.Contains("../../../util/dtd/us-regional-v3-3.dtd", await File.ReadAllTextAsync(deliveredPath), StringComparison.Ordinal);
        Assert.Equal("../12-cover-letters/c%6fver.pdf", delivered.Descendants("leaf").Single().Attributes().Single(attribute => attribute.Name.LocalName == "href").Value);
        var reimported = await restored.ImportAsync();
        Assert.Equal(4, reimported.ImportedSequenceCount);
        Assert.Equal(14, reimported.ImportedDocumentCount);
        var currentOutputPath = Path.Combine(restored.Root, "0001", "m1", "us", "us-regional.xml");
        var modifiedFile = EctdWorkspaceFixture.ReadXml(currentOutputPath).Descendants("leaf").Single().Attribute("modified-file")!.Value;
        var historicalUri = new Uri(new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = currentOutputPath }.Uri, modifiedFile);
        Assert.Equal(deliveredPath, historicalUri.LocalPath);
        Assert.Equal("#overview", historicalUri.Fragment);
    }

    [Fact]
    public async Task ExplicitAndAutomaticLifecycleResolutionStayWithinTheBusinessNode()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        var leaves = (await source.Placements.ListAsync()).Where(leaf => leaf.ImportedSource?.BackboneRelativePath == "index.xml").ToDictionary(leaf => leaf.LeafId);
        var current = leaves["alpha-spec-v2"];
        current.ReviseLifecycleTarget(null);
        await source.Placements.UpdateAsync(current);
        var builder = Builder(source);
        var package = await builder.BuildAsync(new(imported.ApplicationId, "0001"));
        Assert.Equal(leaves["alpha-spec-v1"].Id, Assert.Single(package.IchBackboneLeaves).Lifecycle!.TargetPlacementId);
        current.ReviseLifecycleTarget(leaves["beta-spec-v1"].Id);
        await source.Placements.UpdateAsync(current);
        var failure = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(() => builder.BuildAsync(new(imported.ApplicationId, "0001")));
        Assert.Contains("same business node", failure.Message);
    }

    [Fact]
    public async Task ReadinessLocatesTheIncompleteBusinessNode()
    {
        using var source = new NodeImportWorkspace();
        await source.WriteAsync("0000", Quality(Substance(null, Leaf("missing"))));
        var imported = await source.ImportAsync();
        var readiness = new PublishReadinessService(new UnusedValidation(), Builder(source), new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]), new EctdXmlValidator(),
            new FdaEctd322StandardsProfileProvider(), new EctdValidationEngine(new RegionalEctdRuleSetProvider([])));
        var result = await readiness.GetAsync(new(imported.ApplicationId, "0000"),
            new ValidationReportDto(imported.ApplicationId, "0000", "test", true, [], [], []));
        Assert.False(result.IsReady);
        var issue = Assert.Single(result.Findings);
        Assert.Equal("NodeMetadataIncomplete", issue.Code);
        Assert.StartsWith("nodes.", issue.FieldName);
        Assert.Contains("manufacturer", issue.Message);
    }

    private sealed class UnusedValidation : ISequenceValidationService
    {
        public Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test supplies a completed validation report.");
    }
}
