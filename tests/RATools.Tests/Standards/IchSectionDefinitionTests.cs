using System.Net;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using RATools.Application.Ctd;
using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;
using RATools.Domain.Ctd;

namespace RATools.Tests.Standards;

public sealed class IchSectionDefinitionTests
{
    private const string Substance = "m3-2-s-drug-substance";
    private const string Product = "m3-2-p-drug-product";
    private static SectionDefinitionSet Schema => IchSectionDefinitions.Current;

    [Fact]
    public void SnapshotCoversAllExistingIchSectionsAndMatchesPinnedSourceBytes()
    {
        var existing = FdaEctd322.Root.Children.Where(node => node.SectionPath is "m2" or "m3" or "m4" or "m5")
            .SelectMany(Flatten).ToArray();
        var actual = Schema.Definitions.Values.Where(definition => definition.SectionPath is not null &&
            definition.SectionPath != "m1").ToArray();
        Assert.Equal(158, actual.Length);
        foreach (var node in existing)
            Assert.Equal(node.SectionPath, Schema.Get(node.ElementName).SectionPath);
        Assert.Equal("m2.3", Schema.Get("m2-3-introduction").SectionPath);
        Assert.NotSame(Schema.Get("m2-3-introduction"), Schema.Get("m2-3-quality-overall-summary"));
        Assert.Equal(160, Schema.Definitions.Count);
        Assert.Equal(["m1", "m2", "m3", "m4", "m5"], Schema.Roots.Select(definition => definition.SectionPath));
        Assert.Equal(Schema.SourceSha256, Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "reference", "dtd", "ich-ectd-3-2.dtd")))).ToLowerInvariant());
    }

    [Fact]
    public void RequiredAndOptionalBusinessAttributesMatchTheNineReviewedGroups()
    {
        var business = Schema.Definitions.Values.Where(definition => definition.Attributes.Any(attribute => attribute.Identity)).ToArray();
        Assert.Equal(9, business.Length);
        Assert.All(business, definition => Assert.True(definition.Repeatable));
        Assert.Equal(["manufacturer", "substance"], Schema.Get(Substance).Attributes.Where(attribute => attribute.Required).Select(attribute => attribute.Name));
        Assert.Equal(["manufacturer", "substance"], Schema.Get("m2-3-s-drug-substance").Attributes.Where(attribute => attribute.Required).Select(attribute => attribute.Name));
        Assert.Empty(Schema.Get(Product).ValidateAttributes(new Dictionary<string, string>()));
        Assert.Empty(Schema.Get("m3-2-p-4-control-of-excipients").ValidateAttributes(new Dictionary<string, string>()));
        Assert.Equal(["indication"], Schema.Get("m2-7-3-summary-of-clinical-efficacy").Attributes.Where(attribute => attribute.Required).Select(attribute => attribute.Name));
        Assert.Equal(["indication"], Schema.Get("m5-3-5-reports-of-efficacy-and-safety-studies").Attributes.Where(attribute => attribute.Required).Select(attribute => attribute.Name));
        Assert.All(Schema.Definitions.Values.SelectMany(definition => definition.Attributes).Where(attribute => attribute.Name is "ID" or "xml:lang"),
            attribute => Assert.False(attribute.Identity));
    }

    [Theory]
    [InlineData("manufacturer", "", "RequiredNodeAttributeMissing")]
    [InlineData("manufacturer", "  ", "RequiredNodeAttributeMissing")]
    [InlineData("Manufacturer", "Alpha", "UnknownNodeAttribute")]
    [InlineData("custom-production-site", "Alpha", "UnknownNodeAttribute")]
    [InlineData("manufacturer", "Alpha\u0001", "InvalidNodeAttributeValue")]
    [InlineData("ID", "1-invalid-id", "InvalidNodeAttributeValue")]
    public void InvalidAttributesHaveFieldLevelDiagnostics(string key, string value, string code)
    {
        var values = new Dictionary<string, string> { ["substance"] = "Drug A", ["manufacturer"] = "Alpha", [key] = value };
        Assert.Contains(Schema.Get(Substance).ValidateAttributes(values), issue => issue.Code == code && issue.FieldPath == $"attributes.{key}");
    }

    [Fact]
    public void AttributeValidationPreservesValuesAndDoesNotAcceptCaseInsensitiveAliases()
    {
        var values = new Dictionary<string, string> { ["substance"] = " Drug A ", ["manufacturer"] = "ALPHA" };
        Assert.Empty(Schema.Get(Substance).ValidateAttributes(values));
        Assert.Equal(" Drug A ", values["substance"]);
        Assert.Equal("ALPHA", values["manufacturer"]);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["substance"] = "Drug A", ["Manufacturer"] = "Alpha" };
        Assert.Contains(Schema.Get(Substance).ValidateAttributes(aliases), issue => issue.Code == "RequiredNodeAttributeMissing");
        Assert.Contains(Schema.Get(Substance).ValidateAttributes(aliases), issue => issue.Code == "UnknownNodeAttribute");
    }

    [Fact]
    public void ParentContentEnforcesDtdOrderCardinalityAndExtensionPositions()
    {
        var body = Schema.Get("m3-2-body-of-data");
        Assert.Empty(body.ValidateContent(["leaf", Substance, Substance, Product]));
        Assert.Contains(body.ValidateContent([Product, Substance]), issue => issue.Code == "InvalidNodeContentOrder");
        Assert.Contains(body.ValidateContent([Substance, "leaf"]), issue => issue.Code == "InvalidNodeContentOrder");
        Assert.Contains(body.ValidateContent(["node-extension"]), issue => issue.Code == "InvalidNodeChild");
        Assert.Contains(Schema.Get("m3-quality").ValidateContent(["m3-2-body-of-data", "m3-2-body-of-data"]),
            issue => issue.Code == "NodeCardinalityExceeded");
        Assert.Contains(Schema.Get("m3-quality").ValidateContent(["m5-clinical-study-reports"]), issue => issue.Code == "InvalidNodeChild");
        Assert.Empty(Schema.Get("m4-2-3-2-repeat-dose-toxicity").ValidateContent(["node-extension", "leaf", "node-extension"]));
        Assert.Empty(Schema.Get("m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication")
            .ValidateContent(["node-extension", "node-extension"]));
    }

    [Fact]
    public void ExtensionRequiresTitleAndContentAndCannotContainStandardSections()
    {
        var extension = Schema.Get("node-extension");
        Assert.Null(extension.SectionPath);
        Assert.Empty(extension.ValidateContent(["title", "leaf", "node-extension"]));
        Assert.Contains(extension.ValidateContent([]), issue => issue.Code == "InvalidExtensionContent");
        Assert.Contains(extension.ValidateContent(["title"]), issue => issue.Code == "InvalidExtensionContent");
        Assert.Contains(extension.ValidateContent(["leaf", "title"]), issue => issue.Code == "InvalidExtensionContent");
        Assert.Contains(extension.ValidateContent(["title", Substance]), issue => issue.Code == "InvalidNodeChild");
        Assert.Contains(extension.ValidateAttributes(new Dictionary<string, string> { ["manufacturer"] = "Alpha" }),
            issue => issue.Code == "UnknownNodeAttribute");
    }

    [Fact]
    public void AllHandAuthoredIchNodesSatisfyTheRuntimeSchemaAndTheIndependentDtd()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var paths = Directory.GetFiles(Path.Combine(fixtureRoot, "Publisher", "sequences"), "index.xml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(fixtureRoot, "Publisher", "external"), "*.xml"))
            .Append(Path.Combine(fixtureRoot, "CtdNodes", "attribute-matrix.xml"));
        foreach (var path in paths)
        {
            var errors = new List<string>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, ValidationType = ValidationType.DTD,
                XmlResolver = new IchDtdResolver(), MaxCharactersFromEntities = 1_000_000 };
            settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);
            using var source = new StringReader(File.ReadAllText(path));
            using var reader = XmlReader.Create(source, settings, "file:///fixture/index.xml");
            var document = XDocument.Load(reader);
            Assert.Empty(errors);
            foreach (var node in document.Descendants())
            {
                if (!Schema.Definitions.TryGetValue(node.Name.LocalName, out var definition)) continue;
                var attributes = node.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
                    .ToDictionary(attribute => attribute.Name.Namespace == XNamespace.Xml ? $"xml:{attribute.Name.LocalName}" : attribute.Name.LocalName,
                        attribute => attribute.Value, StringComparer.Ordinal);
                Assert.Empty(definition.ValidateAttributes(attributes));
                Assert.Empty(definition.ValidateContent(node.Elements().Select(child => child.Name.LocalName).ToArray()));
            }
        }
    }

    [Fact]
    public void SchemaRejectsUnknownDefinitionsBrokenParentsAndMutableEnumerationBypasses()
    {
        Assert.Throws<ArgumentException>(() => Schema.Get("invented-section"));
        Assert.Throws<ArgumentException>(() => new SectionDefinitionSet("v1", "exact-v1", "source",
            [Schema.Get(Substance)]));
        string[] choices = ["allowed"];
        var attribute = new SectionAttributeDefinition("country", SectionAttributeValueType.Enumeration, true, true, choices);
        var definition = new SectionDefinition("region", "region", "m1", null, true, CtdNodeKind.Standard,
            true, NodeExtensionPolicy.Forbidden, [attribute], []);
        choices[0] = "bypassed";
        Assert.Empty(definition.ValidateAttributes(new Dictionary<string, string> { ["country"] = "allowed" }));
        Assert.Contains(definition.ValidateAttributes(new Dictionary<string, string> { ["country"] = "bypassed" }),
            issue => issue.Code == "InvalidNodeAttributeChoice");
    }

    private static IEnumerable<SectionDictionaryManualNode> Flatten(SectionDictionaryManualNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));

    private sealed class IchDtdResolver : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (absoluteUri != new Uri("file:///fixture/util/dtd/ich-ectd-3-2.dtd"))
                throw new XmlException("Only the pinned ICH DTD is available.");
            return File.OpenRead(Path.Combine(AppContext.BaseDirectory, "reference", "dtd", "ich-ectd-3-2.dtd"));
        }
    }
}
