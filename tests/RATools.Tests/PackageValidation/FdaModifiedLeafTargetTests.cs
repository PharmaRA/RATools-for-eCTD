using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaModifiedLeafTargetTests
{
    [Theory]
    [InlineData("alias", CheckStatus.Fail)]
    [InlineData("regional", CheckStatus.Pass)]
    public async Task ChecksTheEctdLeafIdWhenXmlIdIsAlsoPresent(string fragment, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", "m1/us/us-regional.xml", "../0000/index.xml#" + fragment, "history-1153-xml-id");
        var history = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.Backbones), item => item.LogicalPath == "0000/index.xml");
        Assert.True(history.ReadComplete);
        Assert.Equal("regional", Assert.Single(history.Leaves).Id);
        Assert.True(history.IdIndex.ContainsKey("alias"));
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1153").Status);
    }

    [Theory]
    [InlineData("../0000/index.xml#regional", "history-1153", CheckStatus.Pass)]
    [InlineData("../0000/index.xml#regional", "history-1153-duplicate", CheckStatus.NotEvaluated)]
    [InlineData("../0000/index.xml#section", "history-1153-nonleaf", CheckStatus.Fail)]
    [InlineData("../0000/absent.xml#regional", "history-1153", CheckStatus.Fail)]
    [InlineData("../0000/m1/us/us-regional.xml#regional", "history-1153", CheckStatus.Fail)]
    [InlineData("../0000/index.xml", "history-1153", CheckStatus.NotEvaluated)]
    [InlineData("../0002/index.xml#regional", "history-1153", CheckStatus.NotEvaluated)]
    [InlineData("index.xml#regional", "valid", CheckStatus.Pass)]
    [InlineData("index.xml#missing", "budget", CheckStatus.NotEvaluated)]
    [InlineData(null, "history-1068", CheckStatus.Pass)]
    [InlineData(null, "truncated", CheckStatus.NotEvaluated)]
    [InlineData(null, "skipped-regional", CheckStatus.NotEvaluated)]
    [InlineData("index.xml#regional", "missing-source", CheckStatus.NotEvaluated)]
    public async Task ResolvesOnlyExactSuppliedTargets(string? reference, string variant, CheckStatus expected)
    {
        var href = variant == "skipped-regional" ? "m1/us/alternate.xml#frag" : "m1/us/us-regional.xml";
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", href, reference, variant);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1153").Status);
    }

    [Theory]
    [InlineData("2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task UsesSelectedDate(string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", "m1/us/us-regional.xml", "index.xml#missing", date: date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1153").Status);
    }

    [Theory]
    [InlineData("old-catalog")]
    [InlineData("ich-profile")]
    public async Task RequiresSelectedCatalogRule(string variant)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", "m1/us/us-regional.xml", "index.xml#missing", variant);
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1153");
    }

    [Theory]
    [InlineData("valid", "HISTORY_NOT_AVAILABLE", CheckStatus.NotEvaluated)]
    [InlineData("history-1153-incomplete", "TARGET_XML_UNINSPECTED", CheckStatus.NotEvaluated)]
    [InlineData("history-1153", "TARGET_LEAF_NOT_FOUND", CheckStatus.Fail)]
    public async Task EvidenceDistinguishesUnavailableUnreadAndMissing(string variant, string code, CheckStatus expected)
    {
        const string reference = "../0000/index.xml#missing";
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", "m1/us/us-regional.xml", reference, variant);
        var backbone = result.Lifecycle.XmlInspections.SelectMany(item => item.Backbones).SingleOrDefault(item => item.LogicalPath == "0000/index.xml");
        if (variant == "valid") Assert.Null(backbone);
        else Assert.Equal(variant == "history-1153", Assert.IsType<RATools.Application.PackageValidation.ParsedBackbone>(backbone).ReadComplete);
        Assert.Contains(result.Lifecycle.Events.Single(item => item.Source.Location.LogicalPath == "0001/index.xml").Findings, finding => finding.Code == code);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1153");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(reference, finding.Actual);
            Assert.Equal("@modified-file", finding.Location.FieldPath);
            Assert.Equal("regional", finding.Location.LeafId);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }
}
