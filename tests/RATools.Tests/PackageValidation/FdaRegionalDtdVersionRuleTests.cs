using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRegionalDtdVersionRuleTests
{
    [Theory]
    [InlineData("explicit", CheckStatus.Pass)]
    [InlineData("omitted", CheckStatus.Fail)]
    [InlineData("foreign", CheckStatus.Fail)]
    [InlineData("wrong", CheckStatus.NotEvaluated)]
    [InlineData("empty", CheckStatus.NotEvaluated)]
    [InlineData("no-root", CheckStatus.NotEvaluated)]
    [InlineData("omitted-broken", CheckStatus.Fail)]
    [InlineData("explicit-broken", CheckStatus.NotEvaluated)]
    public async Task SeparatesExplicitDefaultAndUnavailableRegionalFacts(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        var document = Assert.Single(Target(result).Backbones, doc => doc.Kind == BackboneKind.UsRegional);
        if (scenario is "wrong" or "empty" or "no-root") Assert.Empty(document.Elements);
        else
        {
            var root = Assert.Single(document.Elements, element => element.ParentPath is null);
            Assert.Equal("3.3", root.Attributes["dtd-version"].Value);
            Assert.Equal(scenario is "omitted" or "foreign" or "omitted-broken", root.Attributes["dtd-version"].IsDefault);
            if (scenario is "explicit" or "omitted") Assert.Equal(CheckStatus.Pass, document.DtdStatus);
            if (scenario.EndsWith("-broken", StringComparison.Ordinal)) Assert.False(document.ReadComplete);
        }
        AssertCheck(result, expected, expected == CheckStatus.Fail ? "0001/m1/us/us-regional.xml" : null);
    }

    [Theory]
    [InlineData("multiple-default", "0001/m1/us/us-regional.xml")]
    [InlineData("multiple-alternate", "0001/m1/us/alternate.xml")]
    [InlineData("multiple-second", "0001/m1/us/second.xml")]
    public async Task ChecksEverySelectedRegionalIncludingTheUnreferencedDefault(string scenario, string expectedPath)
    {
        var result = await Inspect(scenario);
        var regional = Target(result).Backbones.Where(doc => doc.Kind == BackboneKind.UsRegional).ToArray();
        Assert.Equal(["0001/m1/us/alternate.xml", "0001/m1/us/second.xml", "0001/m1/us/us-regional.xml"], regional.Select(doc => doc.LogicalPath).ToArray());
        Assert.All(regional, doc => { Assert.True(doc.ReadComplete); Assert.Equal(CheckStatus.Pass, doc.DtdStatus); });
        AssertCheck(result, CheckStatus.Fail, expectedPath);
    }

    [Theory]
    [InlineData("custom", CheckStatus.Pass)]
    [InlineData("custom-omitted", CheckStatus.Fail)]
    public async Task CustomReferencedPathDoesNotRequireTheDefaultFile(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        Assert.Equal("0001/m1/us/alternate.xml", Assert.Single(Target(result).Backbones, doc => doc.Kind == BackboneKind.UsRegional).LogicalPath);
        AssertCheck(result, expected, expected == CheckStatus.Fail ? "0001/m1/us/alternate.xml" : null);
    }

    [Theory]
    [InlineData("invalid-reference", CheckStatus.NotEvaluated)]
    [InlineData("omitted-invalid-reference", CheckStatus.Fail)]
    [InlineData("missing-other", CheckStatus.NotEvaluated)]
    [InlineData("omitted-missing-other", CheckStatus.Fail)]
    [InlineData("none", CheckStatus.NotEvaluated)]
    [InlineData("unselected", CheckStatus.Pass)]
    public async Task IncompleteSelectionCannotPassOrEraseKnownOmission(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        var xml = Target(result);
        if (scenario.EndsWith("invalid-reference", StringComparison.Ordinal))
            Assert.Contains(xml.Checks.SelectMany(check => check.Findings), finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        if (scenario.EndsWith("missing-other", StringComparison.Ordinal))
            Assert.Empty(Assert.Single(xml.Backbones, doc => doc.LogicalPath == "0001/m1/us/z-missing.xml").Elements);
        if (scenario == "none") Assert.DoesNotContain(xml.Backbones, doc => doc.Kind == BackboneKind.UsRegional);
        if (scenario == "unselected") Assert.DoesNotContain(xml.Backbones, doc => doc.LogicalPath.EndsWith("/unselected.xml", StringComparison.Ordinal));
        AssertCheck(result, expected, expected == CheckStatus.Fail ? "0001/m1/us/us-regional.xml" : null);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceDateAndBudgetRemainExplicit(string variant, string? date, CheckStatus expected)
        => AssertCheck(await Inspect("omitted", variant, date), expected);

    [Fact]
    public async Task HistoricalOmissionDoesNotContaminateTarget()
    {
        var result = await Inspect("history");
        var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.LogicalPath == "0000/m1/us/us-regional.xml");
        Assert.True(previous.ReadComplete);
        Assert.True(previous.Elements[0].Attributes["dtd-version"].IsDefault);
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("omitted", "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1445");

    private static PackageXmlInspection Target(PackageDeliveryInspection result)
        => Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest);

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected, string? path = null)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1445");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1445").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("@dtd-version", finding.Location.FieldPath);
        Assert.Equal("/fda-regional:fda-regional[1]", finding.Location.NodePath);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        if (path is not null) Assert.Equal(path, finding.Location.LogicalPath);
        Assert.Equal(finding.Location.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
    }

    private static Task<PackageDeliveryInspection> Inspect(string scenario, string variant = "valid", string? date = "2026-11-20")
    {
        var custom = scenario.StartsWith("custom", StringComparison.Ordinal);
        var multiple = scenario.StartsWith("multiple-", StringComparison.Ordinal);
        var actualVariant = custom ? "skipped-regional" : multiple ? "skipped-regional-with-default" : scenario == "history" ? "history-1445" : variant;
        return FdaExtraneousLeafPathRulesTests.Inspect("new", custom || multiple ? "m1/us/alternate.xml" : "m1/us/us-regional.xml", null,
            actualVariant, date, omittedTargetFile: scenario == "none" ? "m1/us/us-regional.xml" : null,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single();
                leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5");
                if (scenario == "none") { leaf.Remove(); return; }
                if (multiple || scenario.EndsWith("invalid-reference", StringComparison.Ordinal) || scenario.EndsWith("missing-other", StringComparison.Ordinal))
                {
                    var extra = new XElement(leaf); extra.SetAttributeValue("ID", "second-regional");
                    extra.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), multiple ? "m1/us/second.xml" : scenario.EndsWith("invalid-reference", StringComparison.Ordinal) ? "index.xml" : "m1/us/z-missing.xml");
                    leaf.AddAfterSelf(extra);
                }
            },
            editSource: source =>
            {
                var folder = Path.Combine(source, "0001/m1/us");
                var defaultPath = Path.Combine(folder, "us-regional.xml");
                if (multiple) File.Copy(Path.Combine(folder, "alternate.xml"), Path.Combine(folder, "second.xml"));
                if (scenario == "unselected") { File.Copy(defaultPath, Path.Combine(folder, "unselected.xml")); Omit(Path.Combine(folder, "unselected.xml")); }
                if (scenario == "history") Omit(Path.Combine(source, "0000/m1/us/us-regional.xml"));
                var targetPath = custom || scenario == "multiple-alternate" ? Path.Combine(folder, "alternate.xml") :
                    scenario == "multiple-second" ? Path.Combine(folder, "second.xml") : defaultPath;
                if (scenario.Contains("omitted", StringComparison.Ordinal) || multiple || scenario == "foreign") Omit(targetPath);
                if (scenario is "foreign" or "wrong" or "empty")
                {
                    var xml = XDocument.Load(targetPath);
                    if (scenario == "foreign") xml.Root!.SetAttributeValue(XName.Get("dtd-version", "urn:other"), "3.3");
                    else xml.Root!.SetAttributeValue("dtd-version", scenario == "wrong" ? "3.2" : "");
                    xml.Save(targetPath);
                }
                if (scenario == "no-root") File.WriteAllText(targetPath, "<broken");
                if (scenario.EndsWith("-broken", StringComparison.Ordinal)) File.AppendAllText(targetPath, "<broken");
            });
    }

    private static void Omit(string path)
    {
        var xml = XDocument.Load(path); xml.Root!.SetAttributeValue("dtd-version", null); xml.Save(path);
    }
}
