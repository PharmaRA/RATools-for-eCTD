using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredModifiedPathTests
{
    [Theory]
    [InlineData("replace", null, "valid", CheckStatus.Fail)]
    [InlineData("delete", "", "valid", CheckStatus.Fail)]
    [InlineData("append", " ", "valid", CheckStatus.Fail)]
    [InlineData("replace", null, "modified-namespace", CheckStatus.Fail)]
    [InlineData("new", null, "valid", CheckStatus.Pass)]
    [InlineData("replace", "../0000/index.xml#missing", "history-1153", CheckStatus.Pass)]
    [InlineData("replace", "bad path", "valid", CheckStatus.Pass)]
    [InlineData(null, null, "valid", CheckStatus.NotEvaluated)]
    [InlineData("other", null, "valid", CheckStatus.NotEvaluated)]
    [InlineData("replace", null, "truncated", CheckStatus.NotEvaluated)]
    [InlineData("replace", null, "budget", CheckStatus.NotEvaluated)]
    [InlineData("replace", null, "missing-source", CheckStatus.NotEvaluated)]
    [InlineData("new", null, "history-1170", CheckStatus.Pass)]
    public async Task RequiresModifiedFileForLifecycleChanges(string? operation, string? reference, string variant, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect(operation, "m1/us/us-regional.xml", reference, variant);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1170");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("@modified-file", finding.Location.FieldPath);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.Equal(reference, finding.Actual);
        }
        if (variant == "history-1153") Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1153").Status);
    }

    [Theory]
    [InlineData(null, CheckStatus.NotEvaluated)]
    [InlineData("2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", CheckStatus.Fail)]
    public async Task BindsEffectiveDate(string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", "m1/us/us-regional.xml", null, date: date);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == "FDA47-1170").Status);
    }

    [Theory]
    [InlineData("old-catalog")]
    [InlineData("ich-profile")]
    public async Task RequiresSelectedRule(string variant)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("replace", "m1/us/us-regional.xml", null, variant);
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1170");
    }
}
