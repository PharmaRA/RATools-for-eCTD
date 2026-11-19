using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaNodeExtensionRuleTests
{
    [Theory]
    [InlineData("none", CheckStatus.Pass, 0)]
    [InlineData("ich", CheckStatus.Fail, 1)]
    [InlineData("regional", CheckStatus.Fail, 1)]
    [InlineData("both", CheckStatus.Fail, 2)]
    [InlineData("nested", CheckStatus.Fail, 2)]
    [InlineData("no-id", CheckStatus.Fail, 1)]
    [InlineData("foreign", CheckStatus.Pass, 0)]
    [InlineData("ich-broken", CheckStatus.Fail, 1)]
    [InlineData("regional-broken", CheckStatus.Fail, 1)]
    [InlineData("none-broken", CheckStatus.NotEvaluated, 0)]
    public async Task ReportsActualTargetExtensionsWithoutRequiringTheirIdentity(string scenario, CheckStatus expected, int count)
    {
        var result = await Inspect(scenario);
        var xml = Assert.Single(result.Lifecycle.XmlInspections, item => item.InputDigest == result.Files.PrimaryInputDigest);
        var extensions = xml.Backbones.SelectMany(doc => doc.Elements).Where(element => element.LocalName == "node-extension" && element.NamespaceUri.Length == 0).ToArray();
        Assert.Equal(count, extensions.Length);
        if (scenario is "ich" or "regional" or "both" or "nested" or "no-id")
            Assert.All(xml.Backbones, document => Assert.Equal(CheckStatus.Pass, document.DtdStatus));
        if (scenario == "no-id") Assert.Null(Assert.Single(extensions).Attribute("ID"));
        if (scenario == "foreign") Assert.Contains(xml.Backbones.SelectMany(doc => doc.Elements), element => element.LocalName == "node-extension" && element.NamespaceUri == "urn:other");
        if (scenario.EndsWith("-broken", StringComparison.Ordinal))
            Assert.False(Assert.Single(xml.Backbones, doc => doc.Kind == (scenario == "regional-broken" ? BackboneKind.UsRegional : BackboneKind.Ich)).ReadComplete);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1476");
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1476").CheckStatus);
        Assert.All(check.Findings, finding =>
        {
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Null(finding.Location.LeafId);
            Assert.True(finding.Location.Line > 0);
            Assert.Equal(finding.Location.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.Contains(extensions, element => element.NodePath == finding.Location.NodePath && element.Location.LogicalPath == finding.Location.LogicalPath);
        });
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.Fail)]
    public async Task DateSourceAndIncompleteScopesRemainExplicit(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("ich", variant, date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1476").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    [Fact]
    public async Task UnreadRegionalPreventsACompleteNoExtensionPass()
        => Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Inspect("none", "unread")).Files.Checks, check => check.RuleId == "FDA47-1476").Status);

    [Fact]
    public async Task HistoricalExtensionsAreObservedButNotTargetFindings()
    {
        var result = await Inspect("history");
        var history = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.LogicalPath == "0000/index.xml");
        Assert.True(history.ReadComplete);
        Assert.Contains(history.Elements, element => element.Name == "node-extension");
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1476").Status);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("ich", "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1476");

    private static Task<PackageDeliveryInspection> Inspect(string scenario, string variant = "valid", string? date = "2026-11-20")
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, scenario == "history" ? "history-1476" : variant, date,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single();
                leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5");
                if (scenario is "ich" or "both" or "nested" or "no-id" or "foreign" or "ich-broken") AddIndexExtension(xml, scenario);
            },
            editRegional: scenario is "regional" or "both" or "regional-broken" ? xml =>
            {
                var regional = xml.Root!.Element("m1-regional");
                if (regional is null) { regional = new XElement("m1-regional"); xml.Root!.Add(regional); }
                var cover = regional.Element("m1-2-cover-letters");
                if (cover is null)
                {
                    cover = new XElement("m1-2-cover-letters");
                    if (regional.Element("m1-1-forms") is { } forms) forms.AddAfterSelf(cover);
                    else regional.AddFirst(cover);
                }
                cover.Add(Extension("regional-extension", false));
            } : null,
            editIndexText: xml => scenario is "ich-broken" or "none-broken" ? xml + "<broken" : xml,
            editSource: scenario is "history" or "regional-broken" ? source =>
            {
                if (scenario == "regional-broken") { File.AppendAllText(Path.Combine(source, "0001/m1/us/us-regional.xml"), "<broken"); return; }
                var path = Path.Combine(source, "0000/index.xml"); var xml = XDocument.Load(path);
                AddIndexExtension(xml, "ich"); xml.Save(path);
            } : null);

    private static void AddIndexExtension(XDocument xml, string scenario)
    {
        var extension = Extension(scenario == "no-id" ? null : "index-extension", scenario == "nested");
        if (scenario == "foreign") extension.Name = XName.Get("node-extension", "urn:other");
        xml.Root!.Add(new XElement("m2-common-technical-document-summaries", new XElement("m2-2-introduction", extension)));
    }

    private static XElement Extension(string? id, bool nested)
    {
        var leaf = new XElement("leaf", new XAttribute("ID", (id ?? "optional") + "-leaf"), new XAttribute("operation", "new"),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m2/content.txt"), new XElement("title", "Content"));
        return new XElement("node-extension", id is null ? null : new XAttribute("ID", id), new XElement("title", "Extension"),
            nested ? new XElement("node-extension", new XAttribute("ID", "nested-extension"), new XElement("title", "Nested"), leaf) : leaf);
    }
}
