using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Dtos;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Tests.Applications;

namespace RATools.Tests.Publishing;

public sealed class MultiInstanceRoundTripTests
{
    [Fact]
    public async Task ImportPublishInspectDirectoryAndZipReimportAndRepublishPreserveAllFourSequences()
    {
        // The importer currently selects application number from the workspace directory.
        // Keep that explicit identity constant across isolated imports of the same application.
        using var source = new NodeImportWorkspace(applicationNumber: "000001");
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        using var restored = new NodeImportWorkspace(applicationNumber: "000001");
        var outputs = await NodePublishingTests.PublishFixtureAsync(source, restored.Root, imported.ApplicationId);
        var first = await InspectDeliveriesAsync(restored.Root, imported.ApplicationId, outputs);

        var reimported = await restored.ImportAsync();
        Assert.Equal(4, reimported.ImportedSequenceCount);
        Assert.Equal(13, reimported.ImportedDocumentCount);
        Assert.Equal(14, reimported.ImportedPlacementCount);
        await ApplicationImportNodeTests.AssertFixtureAsync(restored, reimported.ApplicationId);
        using var republished = new NodeImportWorkspace(applicationNumber: "000001");
        var secondOutputs = await NodePublishingTests.PublishFixtureAsync(restored, republished.Root, reimported.ApplicationId);
        var second = await InspectDeliveriesAsync(republished.Root, reimported.ApplicationId, secondOutputs);
        Assert.Equal(first.Length, second.Length);
        foreach (var (before, after) in first.Zip(second))
            Assert.True(before == after, "Before: " + before + "\nAfter: " + after);
    }

    [Fact]
    public async Task TamperedDeliveredReferenceFailsIndependentContextValidationAndCannotReimportAsAnAlphaReplacement()
    {
        using var source = new NodeImportWorkspace();
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        using var delivered = new NodeImportWorkspace();
        await NodePublishingTests.PublishFixtureAsync(source, delivered.Root, imported.ApplicationId);
        var path = Path.Combine(delivered.Root, "0001/index.xml");
        var xml = await File.ReadAllTextAsync(path);
        Assert.Contains("../0000/index.xml#alpha-spec-v1", xml, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, xml.Replace("../0000/index.xml#alpha-spec-v1", "../0000/index.xml#beta-spec-v1", StringComparison.Ordinal));
        using var captures = new CaptureRoot();
        var reader = new PackageInputReader(captures.Path);
        await using var first = await reader.ReadAsync(new(imported.ApplicationId, "history", Path.Combine(delivered.Root, "0000")), new());
        await using var target = await reader.ReadAsync(new(imported.ApplicationId, "tampered", Path.Combine(delivered.Root, "0001")), new());
        var result = await InspectAsync(target, [first]);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "TARGET_CONTEXT_MISMATCH");
        Assert.Equal(PackageLeafEffectiveness.Unknown, Assert.Single(result.States, state => state.Address.LeafId == "alpha-spec-v2").Effectiveness);
        var reimported = await delivered.ImportAsync();
        Assert.True(reimported.FailedSequenceCount >= 1);
        Assert.Empty(await delivered.Placements.ListBySequenceAsync(reimported.ApplicationId, "0001"));
        Assert.Empty((await delivered.Nodes.GetSequenceAsync(reimported.ApplicationId, "0001"))?.Nodes ?? []);
    }

    // Used with both in-memory and real PostgreSQL business stores. All validation below
    // consumes physical deliveries and explicit history only, never those stores.
    internal static async Task<string[]> InspectDeliveriesAsync(string root, Guid applicationId,
        IReadOnlyDictionary<string, GeneratedBackboneDto> outputs)
    {
        using var captures = new CaptureRoot();
        var reader = new PackageInputReader(captures.Path);
        var directories = new List<ICapturedPackageInput>();
        var archives = new List<ICapturedPackageInput>();
        var signatures = new List<string>();
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/Publisher/expected.json")));
        try
        {
            foreach (var number in outputs.Keys.Order(StringComparer.Ordinal))
            {
                var directory = await reader.ReadAsync(new(applicationId, "directory-" + number, Path.Combine(root, number)), new());
                directories.Add(directory);
                var archive = await reader.ReadAsync(new(applicationId, "zip-" + number, outputs[number].PackagePath, number), new());
                archives.Add(archive);
                Assert.Equal(directory.Manifest.Files, archive.Manifest.Files);
                Assert.NotEqual(directory.InputDigest, archive.InputDigest); // Original ZIP bytes participate in binding.
                var fromDirectory = await InspectAsync(directory, directories.Take(directories.Count - 1));
                var fromZip = await InspectAsync(archive, archives.Take(archives.Count - 1));
                AssertOracle(fromDirectory, expected.RootElement, number);
                AssertOracle(fromZip, expected.RootElement, number);
                var facts = Facts(fromDirectory);
                Assert.Equal(facts, Facts(fromZip));
                signatures.AddRange(facts);
            }
            // Same filenames under two manufacturers remain different files and content.
            var firstXml = await new PackageXmlInspector().InspectAsync(directories[0], PackageValidationCatalog.UsProfile);
            var ich = firstXml.Backbones.Single(backbone => backbone.Kind == BackboneKind.Ich);
            var alpha = ich.Leaves.Single(leaf => leaf.Id == "alpha-spec-v1");
            var beta = ich.Leaves.Single(leaf => leaf.Id == "beta-spec-v1");
            Assert.NotEqual(alpha.ContextKey, beta.ContextKey);
            Assert.NotEqual(alpha.Href, beta.Href);
            Assert.Equal(Path.GetFileName(alpha.Href), Path.GetFileName(beta.Href));
            var alphaFile = directories[0].Manifest.Files.Single(file => file.LogicalPath == PackageLogicalPath.ResolveReference("0000/index.xml", alpha.Href!).LogicalPath);
            var betaFile = directories[0].Manifest.Files.Single(file => file.LogicalPath == PackageLogicalPath.ResolveReference("0000/index.xml", beta.Href!).LogicalPath);
            Assert.NotEqual(alphaFile.Sha256, betaFile.Sha256);
            return signatures.ToArray();
        }
        finally
        {
            foreach (var input in directories.Concat(archives)) await input.DisposeAsync();
        }
    }

    private static Task<PackageLifecycleInspection> InspectAsync(ICapturedPackageInput target, IEnumerable<ICapturedPackageInput> history)
    {
        var selected = history.ToArray();
        // Import and successful generation alone do not qualify historical trust.
        var baseline = new HistoryBaselineManifest(target.Manifest.ApplicationId, target.Manifest.SequenceNumber,
            selected.Select(input => new HistoryBaselineEntry(input.Manifest.SequenceNumber, HistorySourceKind.ImportedExternal,
                input.SourceId, input.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)));
        return new PackageLifecycleInspector().InspectAsync(new(target, baseline, selected), PackageValidationCatalog.UsProfile);
    }

    private static void AssertOracle(PackageLifecycleInspection inspection, JsonElement oracle, string sequence)
    {
        Assert.DoesNotContain(inspection.Checks.SelectMany(check => check.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        Assert.All(inspection.Checks.Where(check => check.RuleId != "HISTORY-BASELINE"), check => Assert.Equal(CheckStatus.Pass, check.Status));
        Assert.Equal(sequence == "0000", inspection.ResolutionComplete);
        Assert.All(inspection.XmlInspections.SelectMany(xml => xml.Backbones), backbone =>
        {
            Assert.True(backbone.ReadComplete);
            Assert.Equal(CheckStatus.Pass, backbone.DtdStatus);
        });
        Assert.DoesNotContain(inspection.XmlInspections.SelectMany(xml => xml.Checks).SelectMany(check => check.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        Assert.Equal(oracle.GetProperty("effectivePayloadLeaves").GetProperty(sequence).EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal),
            inspection.States.Where(state => state.Effectiveness == PackageLeafEffectiveness.Current && !state.IsRegionalReference).Select(state => Key(state.Address)).Order(StringComparer.Ordinal));
        foreach (var item in oracle.GetProperty("events").EnumerateArray().Where(item => string.CompareOrdinal(item.GetProperty("leaf").GetString()![..4], sequence) <= 0))
        {
            var actual = Assert.Single(inspection.Events, value => Key(value.Source.Address!) == item.GetProperty("leaf").GetString());
            Assert.Equal(item.GetProperty("target").GetString(), Key(actual.Target!));
            Assert.Equal(CheckStatus.Pass, actual.Status);
            if (actual.Source.Operation == "delete") { Assert.Null(actual.Source.Href); Assert.Equal("", actual.Source.Checksum); }
        }
        foreach (var group in oracle.GetProperty("nodeGroups").EnumerateArray())
        {
            var contexts = new HashSet<string?>();
            foreach (var address in group.GetProperty("leaves").EnumerateArray().Select(value => value.GetString()!).Where(address => string.CompareOrdinal(address[..4], sequence) <= 0))
            {
                var leaf = Assert.Single(inspection.Events, item => Key(item.Source.Address!) == address).Source;
                if (!address.Contains("/index.xml#", StringComparison.Ordinal)) continue; // Regional schema qualification remains P6.
                Assert.True(leaf.ContextComplete);
                contexts.Add(leaf.ContextKey);
                var document = inspection.XmlInspections.SelectMany(xml => xml.Backbones).Single(document => document.LogicalPath == address.Split('#')[0]);
                var ancestors = new List<ParsedCtdNode>();
                for (var node = document.Nodes.SingleOrDefault(node => node.NodePath == leaf.ParentNodePath); node is not null;
                     node = document.Nodes.SingleOrDefault(parent => parent.NodePath == node.ParentNodePath)) ancestors.Add(node);
                foreach (var expectedNode in group.GetProperty("ancestors").EnumerateArray())
                {
                    var actual = Assert.Single(ancestors, node => node.DefinitionKey == expectedNode.GetProperty("element").GetString());
                    foreach (var attribute in expectedNode.GetProperty("attributes").EnumerateObject())
                        Assert.Equal(attribute.Value.GetString(), attribute.Name == "ID" ? actual.XmlId : actual.IdentityAttributes[attribute.Name]);
                    if (expectedNode.TryGetProperty("title", out var title)) Assert.Equal(title.GetString(), actual.Title);
                }
            }
            Assert.True(contexts.Count <= 1);
        }
    }

    private static string[] Facts(PackageLifecycleInspection inspection) => inspection.Events.Select(item => JsonSerializer.Serialize(new
    {
        source = Key(item.Source.Address!), target = item.Target is null ? null : Key(item.Target), item.Source.Title,
        item.Source.Operation, item.Source.Href, item.Source.Checksum, item.Source.ContextKey, item.TargetContentLogicalPath, item.Status
    })).Order(StringComparer.Ordinal).ToArray();
    private static string Key(LeafAddress address) => address.SequenceNumber + "/" + address.BackboneRelativePath + "#" + address.LeafId;

    private sealed class CaptureRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ratools-roundtrip-captures-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
