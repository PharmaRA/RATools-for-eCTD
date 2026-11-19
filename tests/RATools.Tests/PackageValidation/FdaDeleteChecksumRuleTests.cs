using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaDeleteChecksumRuleTests
{
    [Theory]
    [InlineData("delete", null, CheckStatus.Pass)]
    [InlineData("delete", "", CheckStatus.Pass)]
    [InlineData("delete", " ", CheckStatus.Fail)]
    [InlineData("delete", "\t", CheckStatus.Fail)]
    [InlineData("delete", "abc", CheckStatus.Fail)]
    [InlineData("new", "abc", CheckStatus.Pass)]
    [InlineData("append", "abc", CheckStatus.Pass)]
    [InlineData("replace", "abc", CheckStatus.Pass)]
    [InlineData("new", null, CheckStatus.Pass)]
    [InlineData("unknown", "abc", CheckStatus.NotEvaluated)]
    [InlineData(null, "abc", CheckStatus.NotEvaluated)]
    public async Task OnlyExplicitDeleteRejectsANonemptyValue(string? operation, string? checksum, CheckStatus expected)
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(checksum: checksum, operation: operation);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1426");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) FdaChecksumTypeRuleTests.AssertFinding(Assert.Single(check.Findings), "@checksum", checksum, "0001/index.xml", "regional");
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("valid", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", "2013-04-30", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("missing-ich-spec", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-19", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-19", CheckStatus.NotEvaluated)]
    public async Task RespectsItsOwnEffectiveDateAndEvidence(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(operation: "delete", variant: variant, date: date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1426").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    public async Task RegionalDeleteIsLocatedIndependently(string variant, CheckStatus expected)
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(operation: "delete", variant: variant, regional: true);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1426");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) FdaChecksumTypeRuleTests.AssertFinding(Assert.Single(check.Findings), "@checksum", "abc", "0001/m1/us/us-regional.xml", "checksum-leaf");
    }

    [Fact]
    public async Task ForeignNamespaceDoesNotCreateChecksumValue()
        => Assert.Equal(CheckStatus.Pass, Assert.Single((await FdaChecksumTypeRuleTests.Inspect(operation: "delete", foreignChecksum: true)).Files.Checks, item => item.RuleId == "FDA47-1426").Status);

    [Fact]
    public async Task HistoricalDeleteIsNotATargetFailure()
        => Assert.Equal(CheckStatus.Pass, Assert.Single((await FdaChecksumTypeRuleTests.Inspect(variant: "history-checksum")).Files.Checks, item => item.RuleId == "FDA47-1426").Status);

    [Fact]
    public async Task DeleteMissingChecksumIsNotAnOmissionFinding()
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(operation: "delete", checksum: null);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1425").Status);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1426").Status);
        Assert.Contains(result.Lifecycle.XmlInspections.SelectMany(item => item.Checks).SelectMany(item => item.Findings), item => item.Code == "DTD_VALIDATION_ERROR");
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1426").CheckStatus);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await FdaChecksumTypeRuleTests.Inspect(variant: "ich-profile")).Checks, item => item.RuleId == "FDA47-1426");
}
