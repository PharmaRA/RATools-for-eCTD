using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaInvalidLeafPathRuleTests
{
    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("alias", "2026-11-26", CheckStatus.Fail)]
    public async Task EffectiveSourceDateAndBudgetAreRequired(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m2/bad name.pdf", null, variant, date,
            contentModule: "m2-common-technical-document-summaries");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1102");
        Assert.Equal(expected, check.Status);
        if (variant == "alias") Assert.Equal("@link:href", Assert.Single(check.Findings).Location.FieldPath);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Fact]
    public async Task XmlCharacterReferenceIsDecodedButNotUriDecoded()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m2/bad name.pdf", null,
            contentModule: "m2-common-technical-document-summaries", editIndexText: text => text.Replace("m2/bad name.pdf", "m2/bad&#x20;name.pdf", StringComparison.Ordinal));
        Assert.Equal("m2/bad name.pdf", Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1102").Findings).Actual);
    }

    [Fact]
    public async Task RegionalContentHasItsOwnLocatedDefect()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, regionalContentHref: "bad name.pdf");
        var finding = Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1102").Findings);
        Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        Assert.Equal("form-leaf", finding.Location.LeafId);
    }

    [Theory]
    [InlineData("m2/bad name.pdf", CheckStatus.Fail)]
    [InlineData("m2/bad\\name.pdf", CheckStatus.Fail)]
    [InlineData("m2/bad:name.pdf", CheckStatus.Fail)]
    [InlineData("m2/bad*name.pdf", CheckStatus.Fail)]
    [InlineData("m2/bad<name.pdf", CheckStatus.Fail)]
    [InlineData("m2/bad>name.pdf", CheckStatus.Fail)]
    [InlineData("m2/bad|name.pdf", CheckStatus.Fail)]
    [InlineData("m2/good.pdf", CheckStatus.NotEvaluated)]
    [InlineData("m2/bad%20name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("m2/good.pdf#bad name", CheckStatus.NotEvaluated)]
    [InlineData("m2/good.pdf?bad name", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/bad name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("mailto:bad name@example.invalid", CheckStatus.NotEvaluated)]
    [InlineData("C:/bad name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("/bad name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("//example.invalid/bad name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("m2/bad%2520name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("m2/bad%ZZ name.pdf", CheckStatus.NotEvaluated)]
    [InlineData("m2/good.pdf#bad|name", CheckStatus.NotEvaluated)]
    [InlineData("../0000/m2/bad name.pdf", CheckStatus.Fail)]
    public async Task ExplicitLocalPathDefectsAreDistinctFromUnqualifiedUriSemantics(string href, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", href, null, contentModule: "m2-common-technical-document-summaries");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1102");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(href, finding.Actual);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("@xlink:href", finding.Location.FieldPath);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal("regional", finding.Location.LeafId);
        }
        else Assert.Empty(check.Findings);
    }
}
