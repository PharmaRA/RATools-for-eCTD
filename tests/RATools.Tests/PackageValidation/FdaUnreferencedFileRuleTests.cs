using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaUnreferencedFileRuleTests
{
    [Theory]
    [InlineData("m2/content.txt", null, "valid", "2022-03-01", CheckStatus.Fail)]
    [InlineData("m3/sub/content.txt", "../../m3/sub/content.txt#part", "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("m4/content.txt", "../../m4/Content.txt", "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData("m1/us/admin.txt", "admin.txt", "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("m1/us/admin.txt", "admin.txt", "wrong-regional-href-namespace", "2026-11-18", CheckStatus.Fail)]
    [InlineData("m2/nested/util/support.txt", null, "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("m5/utility/content.txt", null, "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData("m20/content.txt", null, "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("content.txt", null, "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("m5/content.txt", null, "truncated", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("m5/content.txt", null, "unread", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("m2/content.txt", null, "budget", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("m2/content.txt", null, "valid", "2022-02-28", CheckStatus.NotApplicable)]
    [InlineData("m2/content.txt", null, "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("m2/content.txt", null, "missing-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("m2/content.txt", null, "missing-qa-source", "2026-11-18", CheckStatus.NotEvaluated)]
    public async Task RequiresAnExactBackboneLeafWithinTheModuleFolders(string file, string? regionalHref, string variant, string? date, CheckStatus expected)
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            capturedName: file, regionalContentHref: regionalHref);
        var check = Assert.Single(inspection.Files.Checks, row => row.RuleId == "FDA47-1306");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("0001/" + file, Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task RejectedRegionalEntryLeavesAbsenceUnproven()
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/alternate.xml#%ZZ", null,
            "skipped-regional", capturedName: "m2/content.txt");
        var check = Assert.Single(inspection.Files.Checks, row => row.RuleId == "FDA47-1306");
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }
}
