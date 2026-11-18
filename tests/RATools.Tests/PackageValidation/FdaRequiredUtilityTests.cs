using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredUtilityTests
{
    private const string IchDtd = "util/dtd/ich-ectd-3-2.dtd";

    [Theory]
    [InlineData("valid", IchDtd, null, "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", IchDtd, "elsewhere/ich-ectd-3-2.dtd", "2026-11-18", CheckStatus.Fail)]
    [InlineData("history-1119", IchDtd, null, "2026-11-18", CheckStatus.Fail)]
    [InlineData("truncated", IchDtd, null, "2026-11-18", CheckStatus.Fail)]
    [InlineData("valid", null, IchDtd, "2026-11-18", CheckStatus.Pass)]
    [InlineData("valid", "util/dtd/us-regional-v3-3.dtd", null, "2026-11-18", CheckStatus.Pass)]
    [InlineData("valid", IchDtd, null, "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", IchDtd, null, null, CheckStatus.NotEvaluated)]
    [InlineData("missing-ich-spec", IchDtd, null, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("missing-qa-source", IchDtd, null, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("missing-source", IchDtd, null, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("budget", IchDtd, null, "2026-11-18", CheckStatus.NotEvaluated)]
    public async Task ChecksTargetIchDtdPresence(string variant, string? omitted, string? supplied, string? date, CheckStatus expected)
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            capturedName: supplied, omittedTargetFile: omitted);
        var check = Assert.Single(inspection.Files.Checks, item => item.RuleId == "FDA47-1119");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/" + IchDtd, finding.Location.LogicalPath);
        }
        else Assert.Empty(check.Findings);
        var report = inspection.CreateReport(PackageValidationMode.Development);
        var coverage = Assert.Single(report.Coverage, row => row.RuleId == check.RuleId);
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected, coverage.CheckStatus);
    }
}
