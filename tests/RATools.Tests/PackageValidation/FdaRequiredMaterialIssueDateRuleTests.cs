using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredMaterialIssueDateRuleTests
{
    [Theory]
    [InlineData("fdapmdt1", null, CheckStatus.Fail)]
    [InlineData("fdapmdt1", "", CheckStatus.Fail)]
    [InlineData("fdapmdt1", " \t ", CheckStatus.Fail)]
    [InlineData("fdapmdt1", "20240229", CheckStatus.Pass)]
    [InlineData("fdapmdt1", "abc", CheckStatus.Pass)]
    [InlineData("fdapmdt1", "20230229", CheckStatus.Pass)]
    [InlineData("fdapmdt1", " 20240229 ", CheckStatus.Pass)]
    [InlineData("fdapmdt2", null, CheckStatus.NotApplicable)]
    [InlineData("fdapmdt3", null, CheckStatus.NotApplicable)]
    [InlineData("fdapmdt4", null, CheckStatus.NotApplicable)]
    [InlineData("fdapmdt5", null, CheckStatus.NotApplicable)]
    [InlineData("fdapmdt6", null, CheckStatus.NotApplicable)]
    [InlineData(null, null, CheckStatus.NotEvaluated)]
    [InlineData("", null, CheckStatus.NotEvaluated)]
    [InlineData("unknown", null, CheckStatus.NotEvaluated)]
    [InlineData("FDAPMDT1", null, CheckStatus.NotEvaluated)]
    [InlineData(" fdapmdt1", null, CheckStatus.NotEvaluated)]
    public async Task ParentTypeControlsPresenceRatherThanDateFormat(string? type, string? date, CheckStatus expected)
    {
        var result = await Inspect(type, date);
        var regional = Regional(result);
        Assert.True(regional.ReadComplete);
        var material = Assert.Single(regional.Elements, element => element.Name == "m1-15-2-1-material");
        Assert.Equal(date, material.Attribute("issue-date"));
        AssertCheck(result, expected);
        if (type == "fdapmdt1" && date is "abc" or "20230229" or " 20240229 ")
            Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1554").Status);
    }

    [Theory]
    [InlineData("foreign-date", CheckStatus.Fail)]
    [InlineData("foreign-type", CheckStatus.NotEvaluated)]
    [InlineData("foreign-material", CheckStatus.NotApplicable)]
    [InlineData("wrong-parent", CheckStatus.NotApplicable)]
    [InlineData("empty-group", CheckStatus.NotApplicable)]
    [InlineData("no-group", CheckStatus.NotApplicable)]
    [InlineData("broken-missing", CheckStatus.Fail)]
    [InlineData("broken-present", CheckStatus.NotEvaluated)]
    public async Task SeparatesExactAttributeScopeAndIncompleteEvidence(string scenario, CheckStatus expected)
    {
        var result = await Inspect("fdapmdt1", scenario == "broken-present" ? "20240229" : null, scenario);
        var regional = Regional(result);
        if (scenario.StartsWith("broken-", StringComparison.Ordinal))
        {
            Assert.False(regional.ReadComplete);
            var material = Assert.Single(regional.Elements, element => element.Name == "m1-15-2-1-material");
            Assert.Equal(scenario == "broken-present" ? "20240229" : null, material.Attribute("issue-date"));
            Assert.Equal("fdapmdt1", Assert.Single(regional.Elements, element => element.NodePath == material.ParentPath).Attribute("promotional-material-doc-type"));
        }
        else Assert.True(regional.ReadComplete);
        if (scenario == "foreign-date") Assert.Contains(regional.Elements.SelectMany(element => element.Attributes.Values), attribute => attribute.LocalName == "issue-date" && attribute.NamespaceUri == "urn:other");
        if (scenario == "foreign-type") Assert.Contains(regional.Elements.SelectMany(element => element.Attributes.Values), attribute => attribute.LocalName == "promotional-material-doc-type" && attribute.NamespaceUri == "urn:other");
        if (scenario == "foreign-material") Assert.Contains(regional.Elements, element => element.LocalName == "m1-15-2-1-material" && element.NamespaceUri == "urn:other");
        if (scenario == "wrong-parent")
        {
            var material = Assert.Single(regional.Elements, element => element.Name == "m1-15-2-1-material");
            Assert.Equal("m1-regional", Assert.Single(regional.Elements, element => element.NodePath == material.ParentPath).Name);
        }
        if (scenario == "empty-group") Assert.DoesNotContain(Regional(result).Elements, element => element.Name == "m1-15-2-1-material");
        AssertCheck(result, expected);
    }

    [Theory]
    [InlineData("mixed-pass", CheckStatus.Pass)]
    [InlineData("mixed-fail", CheckStatus.Fail)]
    [InlineData("mixed-unknown", CheckStatus.NotEvaluated)]
    [InlineData("mixed-fail-unknown", CheckStatus.Fail)]
    [InlineData("mixed-unknown-first", CheckStatus.Fail)]
    public async Task MaterialsUseTheirOwnParentGroup(string scenario, CheckStatus expected)
    {
        var result = await Inspect("fdapmdt1", scenario is "mixed-pass" or "mixed-unknown" ? "20240229" : null, scenario);
        Assert.Equal(2, Regional(result).Elements.Count(element => element.Name == "m1-15-2-materials"));
        Assert.True(Regional(result).ReadComplete);
        Assert.Equal(CheckStatus.Fail, Regional(result).DtdStatus);
        AssertCheck(result, expected);
        if (scenario == "mixed-unknown-first")
            Assert.Contains("/m1-15-2-materials[2]/", Assert.Single(Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1681").Findings).Location.NodePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaterMaterialInTheSameGroupCannotBeSkipped()
    {
        var result = await Inspect("fdapmdt1", "20240229", "second-material");
        Assert.Equal(CheckStatus.Pass, Regional(result).DtdStatus);
        AssertCheck(result, CheckStatus.Fail);
        Assert.EndsWith("/m1-15-2-1-material[2]", Assert.Single(Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1681").Findings).Location.NodePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("valid", "2015-06-14", CheckStatus.NotApplicable)]
    [InlineData("valid", "2015-06-15", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task DateSourceAndBudgetRemainBound(string variant, string? date, CheckStatus expected)
        => AssertCheck(await Inspect("fdapmdt1", null, variant: variant, selectionDate: date), expected);

    [Theory]
    [InlineData("fda-m1-attribute-policy")]
    [InlineData("official-fda-promotional-doc-types")]
    public async Task MissingCapturedPolicyOrDictionaryCannotAuthorizeRule(string resource)
        => AssertCheck(await Inspect("fdapmdt1", null, removedResource: resource), CheckStatus.NotEvaluated);

    [Fact]
    public async Task HistoricalMissingDateDoesNotContaminateTarget()
    {
        var result = await Inspect("fdapmdt1", "20240229", "history", "history-1681");
        var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), document => document.LogicalPath == "0000/m1/us/us-regional.xml");
        Assert.True(previous.ReadComplete);
        Assert.Equal(CheckStatus.Pass, previous.DtdStatus);
        Assert.Null(Assert.Single(previous.Elements, element => element.Name == "m1-15-2-1-material").Attribute("issue-date"));
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("fdapmdt1", null, variant: "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1681");

    private static ParsedBackbone Regional(PackageDeliveryInspection result)
        => Assert.Single(Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest).Backbones, document => document.Kind == BackboneKind.UsRegional);

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1681");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1681").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("@issue-date", finding.Location.FieldPath);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        Assert.Equal("0001/m1/us/us-regional.xml", Assert.Single(finding.Location.Files!).LogicalPath);
    }

    private static Task<PackageDeliveryInspection> Inspect(string? type, string? date, string scenario = "valid", string variant = "valid",
        string? selectionDate = "2026-11-20", string? removedResource = null)
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, selectionDate,
            editIndex: xml => { var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5"); },
            editRegional: xml => AddGroups(xml, type, date, scenario),
            editSource: source =>
            {
                if (scenario.StartsWith("broken-", StringComparison.Ordinal)) File.AppendAllText(Path.Combine(source, "0001/m1/us/us-regional.xml"), "<broken");
                if (scenario == "history")
                {
                    var path = Path.Combine(source, "0000/m1/us/us-regional.xml"); var xml = XDocument.Load(path); AddGroups(xml, "fdapmdt1", null, "valid"); xml.Save(path);
                }
            },
            editSnapshot: removedResource is null ? null : snapshot =>
            {
                var original = snapshot.Resources.Single(resource => resource.Key == removedResource);
                return StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != original.Sha256).ToArray() },
                    snapshot.Resources.Where(resource => resource.Key != original.Key), snapshot.AcquiredUtc);
            });

    private static void AddGroups(XDocument xml, string? type, string? date, string scenario)
    {
        if (scenario == "no-group") return;
        var material = new XElement("m1-15-2-1-material", new XAttribute("promotional-material-type", "fdapmt1"), new XAttribute("material-id", "A"));
        material.SetAttributeValue(scenario == "foreign-date" ? XName.Get("issue-date", "urn:other") : "issue-date", scenario == "foreign-date" ? "20240229" : date);
        if (scenario == "foreign-material") { material.Name = XName.Get("m1-15-2-1-material", "urn:other"); material.SetAttributeValue("xmlns", "urn:other"); }
        var group = new XElement("m1-15-2-materials", scenario == "empty-group" ? null : material);
        group.SetAttributeValue(scenario == "foreign-type" ? XName.Get("promotional-material-doc-type", "urn:other") : "promotional-material-doc-type", type);
        if (scenario == "second-material") group.Add(new XElement("m1-15-2-1-material", new XAttribute("promotional-material-type", "fdapmt1"), new XAttribute("material-id", "B")));
        var module = xml.Root!.Element("m1-regional")!;
        if (scenario == "wrong-parent") { module.Add(material); return; }
        var promotional = new XElement("m1-15-promotional-material", new XAttribute("promotional-material-audience-type", "fdapmat2"), group);
        if (scenario.StartsWith("mixed-", StringComparison.Ordinal))
        {
            var otherMaterial = new XElement("m1-15-2-1-material", new XAttribute("promotional-material-type", "fdapmt1"), new XAttribute("material-id", "B"));
            if (scenario == "mixed-fail") otherMaterial.SetAttributeValue("issue-date", "20240229");
            var other = new XElement("m1-15-2-materials", new XAttribute("promotional-material-doc-type", scenario.EndsWith("unknown", StringComparison.Ordinal) || scenario == "mixed-unknown-first" ? "unknown" : "fdapmdt2"), otherMaterial);
            if (scenario == "mixed-unknown-first") promotional.AddFirst(other); else promotional.Add(other);
        }
        module.Add(promotional);
    }
}
