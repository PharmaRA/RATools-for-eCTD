using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaMissingLeafPathRulesTests
{
    [Theory]
    [InlineData("new", null, "valid", CheckStatus.Fail)]
    [InlineData("append", "", "valid", CheckStatus.Fail)]
    [InlineData("new", " ", "valid", CheckStatus.Fail)]
    [InlineData("replace", null, "namespace", CheckStatus.Fail)]
    [InlineData("delete", null, "valid", CheckStatus.Pass)]
    [InlineData("new", "m2/missing.pdf", "alias", CheckStatus.Pass)]
    [InlineData("new", "m2/bad name.pdf", "valid", CheckStatus.Pass)]
    [InlineData(null, null, "valid", CheckStatus.NotEvaluated)]
    [InlineData("unknown", null, "valid", CheckStatus.NotEvaluated)]
    [InlineData("new", null, "missing-source", CheckStatus.NotEvaluated)]
    [InlineData("new", null, "truncated", CheckStatus.NotEvaluated)]
    [InlineData("new", null, "budget", CheckStatus.NotEvaluated)]
    [InlineData("new", "m1/us/us-regional.xml", "history-1136", CheckStatus.Pass)]
    public async Task ChecksRequiredTargetContentPath(string? operation, string? href, string variant, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect(operation, href, null, variant);
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1136");
        Assert.Equal(expected, check.Status);
        if (expected != CheckStatus.Fail) return;
        var finding = Assert.Single(check.Findings);
        Assert.Equal("@xlink:href", finding.Location.FieldPath);
        Assert.Equal("regional", finding.Location.LeafId);
        Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        Assert.Equal(href, finding.Actual);
    }

    [Fact]
    public async Task LocatesTheMissingPathInTheRegionalBackbone()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "regional");
        var finding = Assert.Single(Assert.Single(result.Checks, item => item.RuleId == "FDA47-1136").Findings);
        Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        Assert.Equal("form-leaf", finding.Location.LeafId);
    }

    [Theory]
    [InlineData("skipped-regional", CheckStatus.NotEvaluated)]
    [InlineData("skipped-regional-with-default", CheckStatus.NotEvaluated)]
    [InlineData("skipped-regional-known-failure", CheckStatus.Fail)]
    public async Task SkippedRegionalBackboneCannotEstablishPass(string variant, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/alternate.xml#frag", null, variant);
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1136");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal("known-missing", Assert.Single(check.Findings).Location.LeafId);
        else Assert.Empty(check.Findings);
        if (expected == CheckStatus.NotEvaluated)
        {
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-1051").Status);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-1068").Status);
        }
    }

    [Theory]
    [InlineData("ich-profile")]
    [InlineData("old-catalog")]
    public async Task DoesNotInjectAbsentRules(string variant)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", null, null, variant);
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1136");
    }

    [Theory]
    [InlineData(null, CheckStatus.NotEvaluated)]
    [InlineData("2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", CheckStatus.Fail)]
    public async Task UsesExplicitEffectiveDate(string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", null, null, date: date);
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-1136").Status);
    }
}
