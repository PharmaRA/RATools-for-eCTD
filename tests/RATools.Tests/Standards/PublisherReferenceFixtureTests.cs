using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace RATools.Tests.Standards;

public sealed class PublisherReferenceFixtureTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Publisher");
    private static readonly string Sequences = Path.Combine(Root, "sequences");
    private static readonly XNamespace Xlink = "http://www.w3c.org/1999/xlink";

    [Theory]
    [InlineData("sequences", "files")]
    [InlineData("external", "externalFiles")]
    public void FrozenPackageBytesMatchTheCompleteManifest(string directory, string manifestProperty)
    {
        using var manifest = ReadJson("file-manifest.json");
        var sourceRoot = Path.Combine(Root, directory);
        var entries = manifest.RootElement.GetProperty(manifestProperty).EnumerateArray().ToArray();
        var actualPaths = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')).Order().ToArray();
        Assert.Equal(entries.Select(entry => entry.GetProperty("path").GetString()).Order(), actualPaths);
        foreach (var entry in entries)
        {
            var data = File.ReadAllBytes(Path.Combine(sourceRoot, entry.GetProperty("path").GetString()!));
            Assert.Equal(entry.GetProperty("length").GetInt64(), data.LongLength);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Hex(SHA256.HashData(data)));
            Assert.Equal(entry.GetProperty("md5").GetString(), Hex(MD5.HashData(data)));
        }
    }

    [Fact]
    public void EveryHandAuthoredBackboneAndOfficialExcerptPassesTheSourceDtd()
    {
        var paths = Directory.GetFiles(Sequences, "*.xml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root, "external"), "*.xml"));
        foreach (var path in paths)
        {
            var errors = ValidateXml(path);
            Assert.True(errors.Count == 0, $"{path}: {string.Join(" | ", errors)}");
        }
    }

    [Fact]
    public void EveryPayloadAndIndexChecksumResolvesToTheActualFileBytes()
    {
        foreach (var path in Directory.GetFiles(Sequences, "*.xml", SearchOption.AllDirectories))
        {
            foreach (var leaf in LoadXml(path).Descendants("leaf"))
            {
                if ((string?)leaf.Attribute("operation") == "delete")
                {
                    Assert.Null(leaf.Attribute(Xlink + "href"));
                    continue;
                }
                var href = (string?)leaf.Attribute(Xlink + "href");
                Assert.False(string.IsNullOrWhiteSpace(href));
                var file = new Uri(new Uri(Path.GetFullPath(path)), href).LocalPath;
                Assert.True(File.Exists(file), file);
                Assert.Equal((string?)leaf.Attribute("checksum"), Hex(MD5.HashData(File.ReadAllBytes(file))));
            }
        }
        foreach (var directory in Directory.GetDirectories(Sequences))
        {
            Assert.Equal(File.ReadAllText(Path.Combine(directory, "index-md5.txt")),
                Hex(MD5.HashData(File.ReadAllBytes(Path.Combine(directory, "index.xml")))));
        }
    }

    [Fact]
    public void NodeAndLeafContextMatchesTheHandAuthoredBusinessOracle()
    {
        using var expected = ReadJson("expected.json");
        var leaves = ReadLeaves();
        var expectedAddresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in expected.RootElement.GetProperty("nodeGroups").EnumerateArray())
        {
            foreach (var address in group.GetProperty("leaves").EnumerateArray().Select(value => value.GetString()!))
            {
                Assert.True(expectedAddresses.Add(address), address);
                var leaf = leaves[address];
                var section = leaf.Ancestors().First(node => node.Name.LocalName.StartsWith('m'));
                Assert.StartsWith(group.GetProperty("section").GetString()!.Replace('.', '-') + "-", section.Name.LocalName);
                foreach (var ancestor in group.GetProperty("ancestors").EnumerateArray())
                {
                    var node = Assert.Single(leaf.Ancestors(ancestor.GetProperty("element").GetString()!));
                    foreach (var attribute in ancestor.GetProperty("attributes").EnumerateObject())
                        Assert.Equal(attribute.Value.GetString(), (string?)node.Attribute(attribute.Name));
                    if (ancestor.TryGetProperty("title", out var title))
                        Assert.Equal(title.GetString(), (string?)node.Element("title"));
                }
            }
        }
        var payloadAndDeleteLeaves = leaves.Where(item => ((string?)item.Value.Attribute(Xlink + "href"))?.EndsWith(".xml", StringComparison.Ordinal) != true)
            .Select(item => item.Key).Order().ToArray();
        Assert.Equal(expectedAddresses.Order(), payloadAndDeleteLeaves);
        Assert.NotSame(leaves["0000/index.xml#overview"], leaves["0000/m1/us/us-regional.xml#overview"]);
    }

    [Fact]
    public void LifecycleEdgesUseExactXmlAddressesAndHaveExplicitEffectiveSets()
    {
        using var expected = ReadJson("expected.json");
        var leaves = ReadLeaves();
        foreach (var item in expected.RootElement.GetProperty("events").EnumerateArray())
        {
            var address = item.GetProperty("leaf").GetString()!;
            var leaf = leaves[address];
            Assert.Equal(item.GetProperty("operation").GetString(), (string?)leaf.Attribute("operation"));
            var source = new Uri("https://fixture.invalid/" + address.Split('#')[0]);
            var target = new Uri(source, (string)leaf.Attribute("modified-file")!);
            var targetAddress = target.AbsolutePath.TrimStart('/') + target.Fragment;
            Assert.Equal(item.GetProperty("target").GetString(), targetAddress);
            Assert.True(leaves.ContainsKey(targetAddress), targetAddress);
            var effective = expected.RootElement.GetProperty("effectivePayloadLeaves").GetProperty(address[..4])
                .EnumerateArray().Select(value => value.GetString()!).ToHashSet();
            Assert.Equal(item.GetProperty("targetRemainsEffective").GetBoolean(), effective.Contains(targetAddress));
        }
        foreach (var sequence in expected.RootElement.GetProperty("effectivePayloadLeaves").EnumerateObject())
        {
            var addresses = sequence.Value.EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Equal(addresses.Length, addresses.Distinct().Count());
            foreach (var address in addresses)
            {
                Assert.True(string.CompareOrdinal(address[..4], sequence.Name) <= 0);
                Assert.NotEqual("delete", (string?)leaves[address].Attribute("operation"));
            }
        }
    }

    [Fact]
    public void SyntheticPdfsContainThePublishedTestSentences()
    {
        using var payloads = ReadJson("payloads.json");
        foreach (var payload in payloads.RootElement.EnumerateObject())
        {
            using var document = PdfDocument.Open(Path.Combine(Sequences, payload.Name));
            Assert.Equal(1, document.NumberOfPages);
            var text = string.Join(" ", document.GetPage(1).GetWords().Select(word => word.Text));
            Assert.Contains(payload.Value.GetString()!, text);
            Assert.Contains("No patient, sponsor or submission data.", text);
        }
    }

    [Fact]
    public void OfficialExamplesRetainTheirIndependentAttributesAndExtension()
    {
        var substanceNodes = LoadXml(Path.Combine(Root, "external", "ich-example-6-4.xml"))
            .Descendants("m3-2-s-drug-substance").ToArray();
        Assert.Equal(["My Supplier", "Bulk Company 2", "Drug company 2"],
            substanceNodes.Select(node => (string?)node.Attribute("manufacturer")));
        Assert.Equal(["Acetaminophen", "Acetaminophen", "Codeine"],
            substanceNodes.Select(node => (string?)node.Attribute("substance")));
        var extension = Assert.Single(LoadXml(Path.Combine(Root, "external", "ich-example-6-5.xml")).Descendants("node-extension"));
        Assert.Equal("special-summary", (string?)extension.Element("title"));
        Assert.Equal("a123456", (string?)Assert.Single(extension.Elements("leaf")).Attribute("ID"));
    }

    [Theory]
    [InlineData("missing-manufacturer")]
    [InlineData("duplicate-xml-id")]
    public void DeliberateStructureDefectsFailTheIndependentDtdReader(string id)
    {
        using var cases = ReadJson("invalid-cases.json");
        var mutation = cases.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id).GetProperty("mutation");
        var text = File.ReadAllText(Path.Combine(Sequences, mutation.GetProperty("path").GetString()!))
            .Replace(mutation.GetProperty("old").GetString()!, mutation.GetProperty("new").GetString());
        Assert.NotEmpty(ValidateXml(text, isContent: true));
    }

    private static Dictionary<string, XElement> ReadLeaves() => Directory.GetFiles(Sequences, "*.xml", SearchOption.AllDirectories)
        .SelectMany(path => LoadXml(path).Descendants("leaf").Select(leaf => new
        {
            Address = Path.GetRelativePath(Sequences, path).Replace('\\', '/') + "#" + (string?)leaf.Attribute("ID"),
            Leaf = leaf
        })).ToDictionary(item => item.Address, item => item.Leaf, StringComparer.Ordinal);

    private static JsonDocument ReadJson(string name) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, name)));
    private static string Hex(byte[] digest) => Convert.ToHexString(digest).ToLowerInvariant();
    private static XDocument LoadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static List<string> ValidateXml(string input, bool isContent = false)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse, ValidationType = ValidationType.DTD,
            XmlResolver = new SourceDtdResolver(), MaxCharactersFromEntities = 1_000_000
        };
        settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);
        using var source = new StringReader(isContent ? input : File.ReadAllText(input));
        using var reader = XmlReader.Create(source, settings,
            isContent ? "file:///fixture/index.xml" : new Uri(Path.GetFullPath(input)).AbsoluteUri);
        while (reader.Read()) { }
        return errors;
    }

    private sealed class SourceDtdResolver : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var name = Path.GetFileName(absoluteUri.LocalPath);
            if (!absoluteUri.IsFile || name is not ("ich-ectd-3-2.dtd" or "us-regional-v3-3.dtd"))
                throw new XmlException("Only frozen fixture DTDs are available");
            return File.OpenRead(Path.Combine(Sequences, "0000", "util", "dtd", name));
        }
    }
}
