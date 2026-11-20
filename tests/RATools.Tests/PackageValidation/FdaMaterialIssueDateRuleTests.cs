using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Tests.PackageValidation;

public sealed class FdaMaterialIssueDateRuleTests
{
    [Theory]
    [InlineData(null, CheckStatus.Pass)]
    [InlineData("20120415", CheckStatus.Pass)]
    [InlineData("20240229", CheckStatus.Pass)]
    [InlineData("20000229", CheckStatus.Pass)]
    [InlineData("99991231", CheckStatus.Pass)]
    [InlineData("00010101", CheckStatus.Pass)]
    [InlineData("19000229", CheckStatus.Fail)]
    [InlineData("20230229", CheckStatus.Fail)]
    [InlineData("20230431", CheckStatus.Fail)]
    [InlineData("20231301", CheckStatus.Fail)]
    [InlineData("00000000", CheckStatus.Fail)]
    [InlineData("", CheckStatus.Fail)]
    [InlineData(" ", CheckStatus.Fail)]
    [InlineData(" 20240229", CheckStatus.Fail)]
    [InlineData("20240229 ", CheckStatus.Fail)]
    [InlineData("2024-02-29", CheckStatus.Fail)]
    [InlineData("2024022", CheckStatus.Fail)]
    [InlineData("202402290", CheckStatus.Fail)]
    [InlineData("２０２４０２２９", CheckStatus.Fail)]
    [InlineData("\t20240229", CheckStatus.Fail)]
    [InlineData("20240229\n", CheckStatus.Fail)]
    public async Task RequiresEightAsciiDigitsAndAnActualCalendarDate(string? date, CheckStatus expected)
    {
        var result = await Inspect(date);
        var regional = Regional(result);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        var material = Assert.Single(regional.Elements, element => element.Name == "m1-15-2-1-material");
        Assert.Equal(date, material.Attribute("issue-date"));
        AssertCheck(result, expected, date);
    }

    [Theory]
    [InlineData("reference", "20240229", CheckStatus.Pass)]
    [InlineData("no-double-decode", "&#50;0240229", CheckStatus.Fail)]
    public async Task UsesXmlDecodedAttributeValueExactlyOnce(string scenario, string decoded, CheckStatus expected)
    {
        var result = await Inspect("20240229", scenario);
        Assert.Equal(decoded, Assert.Single(Regional(result).Elements, element => element.Name == "m1-15-2-1-material").Attribute("issue-date"));
        AssertCheck(result, expected, decoded);
    }

    [Theory]
    [InlineData("foreign-attribute", "bad", CheckStatus.Pass)]
    [InlineData("foreign-material", "bad", CheckStatus.Pass)]
    [InlineData("wrong-parent", "bad", CheckStatus.Pass)]
    [InlineData("none", "bad", CheckStatus.Pass)]
    [InlineData("non2253", "20240229", CheckStatus.Pass)]
    [InlineData("non2253", "20230229", CheckStatus.Fail)]
    [InlineData("broken", "20230229", CheckStatus.Fail)]
    [InlineData("broken", "20240229", CheckStatus.NotEvaluated)]
    public async Task SeparatesFormatFromScopeAndDeliveryPolicy(string scenario, string date, CheckStatus expected)
    {
        var result = await Inspect(date, scenario);
        if (scenario == "broken") Assert.False(Regional(result).ReadComplete);
        AssertCheck(result, expected, date);
    }

    [Theory]
    [InlineData("valid", "2021-03-14", CheckStatus.NotApplicable)]
    [InlineData("valid", "2021-03-15", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task RespectsOwnDateSourceAndBudget(string variant, string? selectionDate, CheckStatus expected)
        => AssertCheck(await Inspect("20230229", variant: variant, selectionDate: selectionDate), expected);

    [Fact]
    public async Task MissingM1SpecificationCannotAuthorizeTheRule()
        => AssertCheck(await Inspect("20230229", omitM1: true), CheckStatus.NotEvaluated);

    [Fact]
    public async Task ActualHistoricalInvalidDateDoesNotContaminateTarget()
    {
        var result = await Inspect("20240229", "history", "history-1554");
        var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), document => document.LogicalPath == "0000/m1/us/us-regional.xml");
        Assert.True(previous.ReadComplete);
        Assert.Equal(CheckStatus.Pass, previous.DtdStatus);
        Assert.Equal("20230229", Assert.Single(previous.Elements, element => element.Name == "m1-15-2-1-material").Attribute("issue-date"));
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("bad", variant: "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1554");

    private static ParsedBackbone Regional(PackageDeliveryInspection result)
        => Assert.Single(Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest).Backbones, document => document.Kind == BackboneKind.UsRegional);

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected, string? date = null)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1554");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1554").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Equal("@issue-date", finding.Location.FieldPath);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        Assert.True(finding.Location.Column > 0);
        Assert.Equal("0001/m1/us/us-regional.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        if (date is not null) Assert.Equal(date, finding.Actual);
    }

    private static Task<PackageDeliveryInspection> Inspect(string? date, string scenario = "valid", string variant = "valid",
        string? selectionDate = "2026-11-20", bool omitM1 = false)
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, selectionDate,
            editIndex: xml => { var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5"); },
            editRegional: xml => AddMaterial(xml, date, scenario),
            editSource: source =>
            {
                var path = Path.Combine(source, "0001/m1/us/us-regional.xml");
                if (scenario == "broken") File.AppendAllText(path, "<broken");
                if (scenario is "reference" or "no-double-decode")
                    File.WriteAllText(path, File.ReadAllText(path).Replace("issue-date=\"20240229\"", scenario == "reference" ? "issue-date=\"&#50;0240229\"" : "issue-date=\"&amp;#50;0240229\"", StringComparison.Ordinal));
                if (scenario == "history")
                {
                    var previousPath = Path.Combine(source, "0000/m1/us/us-regional.xml"); var previous = XDocument.Load(previousPath);
                    AddMaterial(previous, "20230229", "valid"); previous.Save(previousPath);
                    var indexPath = Path.Combine(source, "0000/index.xml"); var index = XDocument.Load(indexPath);
                    var leaf = index.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5"); index.Save(indexPath);
                }
            },
            editSnapshot: !omitM1 ? null : snapshot =>
            {
                var original = snapshot.Resources.Single(resource => resource.Key == "official-fda-m1-specification");
                return StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != original.Sha256).ToArray() },
                    snapshot.Resources.Where(resource => resource.Key != original.Key), snapshot.AcquiredUtc);
            });

    private static void AddMaterial(XDocument xml, string? date, string scenario)
    {
        if (scenario == "none") return;
        var material = new XElement("m1-15-2-1-material", new XAttribute("promotional-material-type", "fdapmt1"), new XAttribute("material-id", "A"));
        material.SetAttributeValue(scenario == "foreign-attribute" ? XName.Get("issue-date", "urn:other") : "issue-date", date);
        if (scenario == "foreign-material") { material.Name = XName.Get("m1-15-2-1-material", "urn:other"); material.SetAttributeValue("xmlns", "urn:other"); }
        var module = xml.Root!.Element("m1-regional")!;
        if (scenario == "wrong-parent") { module.Add(material); return; }
        module.Add(new XElement("m1-15-promotional-material", new XAttribute("promotional-material-audience-type", "fdapmat2"),
            new XElement("m1-15-2-materials", new XAttribute("promotional-material-doc-type", scenario == "non2253" ? "fdapmdt2" : "fdapmdt1"), material)));
    }
}
