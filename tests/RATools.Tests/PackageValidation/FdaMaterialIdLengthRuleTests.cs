using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Tests.PackageValidation;

public sealed class FdaMaterialIdLengthRuleTests
{
    [Theory]
    [InlineData("ascii", 29, CheckStatus.Pass)]
    [InlineData("ascii", 30, CheckStatus.Pass)]
    [InlineData("ascii", 31, CheckStatus.Fail)]
    [InlineData("nonbmp", 29, CheckStatus.Pass)]
    [InlineData("nonbmp", 30, CheckStatus.Pass)]
    [InlineData("nonbmp", 31, CheckStatus.Fail)]
    [InlineData("combining", 30, CheckStatus.Pass)]
    [InlineData("combining", 31, CheckStatus.Fail)]
    [InlineData("reference", 30, CheckStatus.Pass)]
    [InlineData("reference", 31, CheckStatus.Fail)]
    [InlineData("entity", 30, CheckStatus.Pass)]
    [InlineData("entity", 31, CheckStatus.Fail)]
    [InlineData("spaces", 30, CheckStatus.Pass)]
    [InlineData("spaces", 31, CheckStatus.Fail)]
    [InlineData("literal-tab", 30, CheckStatus.Pass)]
    [InlineData("referenced-tab", 30, CheckStatus.Pass)]
    public async Task CountsDecodedXmlScalarsWithoutTrimmingOrNormalization(string scenario, int count, CheckStatus expected)
    {
        var result = await Inspect(scenario, count);
        var regional = Regional(result);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        var material = Assert.Single(regional.Elements, element => element.Name == "m1-15-2-1-material");
        var value = material.Attributes["material-id"];
        Assert.False(value.IsDefault);
        Assert.Equal(count, value.Value.EnumerateRunes().Count());
        if (scenario == "nonbmp") Assert.Equal(count * 2, value.Value.Length);
        if (scenario == "literal-tab") Assert.EndsWith(" ", value.Value, StringComparison.Ordinal);
        if (scenario == "referenced-tab") Assert.EndsWith("\t", value.Value, StringComparison.Ordinal);
        AssertCheck(result, expected, value.Value);
    }

    [Theory]
    [InlineData("missing", CheckStatus.Pass)]
    [InlineData("empty", CheckStatus.Pass)]
    [InlineData("foreign-attribute", CheckStatus.Pass)]
    [InlineData("foreign-material", CheckStatus.Pass)]
    [InlineData("wrong-parent", CheckStatus.Pass)]
    [InlineData("none", CheckStatus.Pass)]
    [InlineData("broken-long", CheckStatus.Fail)]
    [InlineData("broken-short", CheckStatus.NotEvaluated)]
    public async Task KeepsRequirednessScopeAndIncompleteEvidenceSeparate(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario, 31);
        if (scenario.StartsWith("broken-", StringComparison.Ordinal)) Assert.False(Regional(result).ReadComplete);
        if (scenario == "missing") Assert.DoesNotContain(Regional(result).Elements.SelectMany(element => element.Attributes.Values), attribute => attribute.Name == "material-id");
        AssertCheck(result, expected);
    }

    [Theory]
    [InlineData("valid", "2015-06-14", CheckStatus.NotApplicable)]
    [InlineData("valid", "2015-06-15", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceDateAndBudgetRemainBound(string variant, string? date, CheckStatus expected)
        => AssertCheck(await Inspect("ascii", 31, variant, date), expected);

    [Fact]
    public async Task HistoryDoesNotSupplyTargetLengthFindings()
    {
        var result = await Inspect("history", 30, "history-1550");
        var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), document => document.LogicalPath == "0000/m1/us/us-regional.xml");
        Assert.True(previous.ReadComplete);
        Assert.Equal(31, Assert.Single(previous.Elements, element => element.Name == "m1-15-2-1-material").Attribute("material-id")!.Length);
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task MissingM1SpecificationCannotAuthorizeTheRule()
        => AssertCheck(await Inspect("ascii", 31, omitM1: true), CheckStatus.NotEvaluated);

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("ascii", 31, "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1550");

    private static ParsedBackbone Regional(PackageDeliveryInspection result)
        => Assert.Single(Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest).Backbones, document => document.Kind == BackboneKind.UsRegional);

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected, string? value = null)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1550");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1550").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("@material-id", finding.Location.FieldPath);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        Assert.True(finding.Location.Column > 0);
        Assert.EndsWith("/m1-15-2-1-material[1]", finding.Location.NodePath, StringComparison.Ordinal);
        Assert.Equal("0001/m1/us/us-regional.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        if (value is not null) Assert.Equal(value, finding.Actual);
    }

    private static Task<PackageDeliveryInspection> Inspect(string scenario, int count, string variant = "valid", string? date = "2026-11-20", bool omitM1 = false)
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml => { var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5"); },
            editRegional: xml => AddMaterial(xml, scenario),
            editSource: source =>
            {
                var raw = scenario switch
                {
                    "nonbmp" => string.Concat(Enumerable.Repeat("\U00010400", count)),
                    "combining" => string.Concat(Enumerable.Repeat("e\u0301", 15)) + (count == 31 ? "A" : ""),
                    "reference" => string.Concat(Enumerable.Repeat("&#x10400;", count)),
                    "entity" => string.Concat(Enumerable.Repeat("&amp;", count)),
                    "spaces" => " " + new string('A', 29) + (count == 31 ? " " : ""),
                    "literal-tab" => new string('A', 29) + "\t",
                    "referenced-tab" => new string('A', 29) + "&#x9;",
                    "empty" => "", "broken-short" => "A", _ => new string('A', count)
                };
                var path = Path.Combine(source, "0001/m1/us/us-regional.xml");
                File.WriteAllText(path, File.ReadAllText(path).Replace("MATERIAL-MARKER", raw, StringComparison.Ordinal) + (scenario.StartsWith("broken-", StringComparison.Ordinal) ? "<broken" : ""));
                if (scenario == "history")
                {
                    var previousPath = Path.Combine(source, "0000/m1/us/us-regional.xml"); var previous = XDocument.Load(previousPath);
                    AddMaterial(previous, "ascii"); previous.Save(previousPath);
                    File.WriteAllText(previousPath, File.ReadAllText(previousPath).Replace("MATERIAL-MARKER", new string('A', 31), StringComparison.Ordinal));
                }
            },
            editSnapshot: !omitM1 ? null : snapshot =>
            {
                var original = snapshot.Resources.Single(resource => resource.Key == "official-fda-m1-specification");
                return StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != original.Sha256).ToArray() },
                    snapshot.Resources.Where(resource => resource.Key != original.Key), snapshot.AcquiredUtc);
            });

    private static void AddMaterial(XDocument xml, string scenario)
    {
        if (scenario == "none") return;
        var material = new XElement("m1-15-2-1-material", new XAttribute("promotional-material-type", "fdapmt1"));
        if (scenario != "missing") material.SetAttributeValue(scenario == "foreign-attribute" ? XName.Get("material-id", "urn:other") : "material-id", "MATERIAL-MARKER");
        if (scenario == "foreign-material") { material.Name = XName.Get("m1-15-2-1-material", "urn:other"); material.SetAttributeValue("xmlns", "urn:other"); }
        var module = xml.Root!.Element("m1-regional")!;
        if (scenario == "wrong-parent") { module.Add(material); return; }
        module.Add(new XElement("m1-15-promotional-material", new XAttribute("promotional-material-audience-type", "fdapmat2"),
            new XElement("m1-15-2-materials", new XAttribute("promotional-material-doc-type", "fdapmdt1"), material)));
    }
}
