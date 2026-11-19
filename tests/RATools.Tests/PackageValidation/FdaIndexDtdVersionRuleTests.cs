using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaIndexDtdVersionRuleTests
{
    [Theory]
    [InlineData("explicit", CheckStatus.Pass)]
    [InlineData("omitted", CheckStatus.Fail)]
    [InlineData("foreign", CheckStatus.Fail)]
    [InlineData("wrong", CheckStatus.NotEvaluated)]
    [InlineData("empty", CheckStatus.NotEvaluated)]
    [InlineData("space", CheckStatus.NotEvaluated)]
    [InlineData("no-doctype", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("no-root", CheckStatus.NotEvaluated)]
    [InlineData("explicit-broken", CheckStatus.Pass)]
    [InlineData("omitted-broken", CheckStatus.Fail)]
    public async Task DistinguishesExplicitDefaultAndUnavailableRootFacts(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        var index = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.Kind == BackboneKind.Ich);
        if (scenario is "explicit" or "omitted" or "foreign" or "explicit-broken" or "omitted-broken")
        {
            var root = Assert.Single(index.Elements, element => element.ParentPath is null);
            var version = root.Attributes["dtd-version"];
            Assert.Equal("3.2", version.Value);
            Assert.Equal(scenario is "omitted" or "foreign" or "omitted-broken", version.IsDefault);
            if (scenario is "explicit" or "omitted") Assert.Equal(CheckStatus.Pass, index.DtdStatus);
            if (scenario.EndsWith("-broken", StringComparison.Ordinal)) Assert.False(index.ReadComplete);
        }
        else Assert.Empty(index.Elements);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1442");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal("/ectd:ectd[1]", finding.Location.NodePath);
            Assert.Equal("@dtd-version", finding.Location.FieldPath);
            Assert.Null(finding.Location.LeafId);
            Assert.True(finding.Location.Line > 0);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.Fail)]
    public async Task PreservesDateSourceAndBudgetBoundaries(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("omitted", variant, date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1442").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    [Fact]
    public async Task UnreadRegionalCannotEraseExplicitIndexVersion()
        => Assert.Equal(CheckStatus.Pass, Assert.Single((await Inspect("explicit", "unread")).Files.Checks, item => item.RuleId == "FDA47-1442").Status);

    [Fact]
    public async Task HistoricalOmissionIsNotATargetFinding()
    {
        var result = await Inspect("explicit", "history-version");
        var history = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.LogicalPath == "0000/index.xml");
        Assert.True(history.ReadComplete);
        Assert.True(history.Elements[0].Attributes["dtd-version"].IsDefault);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1442").Status);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("omitted", "ich-profile")).Files.Checks, item => item.RuleId == "FDA47-1442");

    [Theory]
    [InlineData("explicit", CheckStatus.Pass)]
    [InlineData("omitted", CheckStatus.Fail)]
    [InlineData("no-root", CheckStatus.NotEvaluated)]
    public async Task ReportRetainsActualRuleCoverage(string scenario, CheckStatus expected)
        => Assert.Equal(expected, Assert.Single((await Inspect(scenario)).CreateReport(PackageValidationMode.Development).Coverage,
            item => item.RuleId == "FDA47-1442").CheckStatus);

    private static Task<PackageDeliveryInspection> Inspect(string scenario, string variant = "valid", string? date = "2026-11-20")
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single();
                leaf.SetAttributeValue("checksum", new string('0', 32));
                leaf.SetAttributeValue("checksum-type", "md5");
                if (scenario is "omitted" or "omitted-broken" or "foreign") xml.Root!.SetAttributeValue("dtd-version", null);
                if (scenario == "foreign") xml.Root!.SetAttributeValue(XName.Get("dtd-version", "urn:other"), "3.2");
                if (scenario is "wrong" or "empty" or "space") xml.Root!.SetAttributeValue("dtd-version", scenario == "wrong" ? "3.3" : scenario == "empty" ? "" : " ");
                if (scenario == "no-doctype") xml.DocumentType!.Remove();
                if (scenario == "wrong-root") xml.Root!.Name = "wrong";
            },
            editIndexText: xml => scenario == "no-root" ? "<ectd>" : scenario.EndsWith("-broken", StringComparison.Ordinal) ? xml + "<broken" : xml,
            editSource: variant == "history-version" ? source =>
            {
                var path = Path.Combine(source, "0000/index.xml");
                var xml = XDocument.Load(path);
                xml.Root!.SetAttributeValue("dtd-version", null);
                xml.Save(path);
            } : null);
}
