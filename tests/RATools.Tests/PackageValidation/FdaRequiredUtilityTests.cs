using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredUtilityTests
{
    private const string IchDtd = "util/dtd/ich-ectd-3-2.dtd";

    [Theory]
    [InlineData("valid", false, false, "2013-04-30", CheckStatus.Pass)]
    [InlineData("valid", true, false, "2013-04-30", CheckStatus.Fail)]
    [InlineData("history-1130", true, false, "2026-11-18", CheckStatus.Fail)]
    [InlineData("truncated", true, false, "2026-11-18", CheckStatus.Fail)]
    [InlineData("valid", false, true, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("missing-qa-source", true, false, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("missing-dtd-checksum", true, false, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("missing-source", true, false, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("valid", true, false, "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", true, false, null, CheckStatus.NotEvaluated)]
    [InlineData("budget", true, false, "2026-11-18", CheckStatus.NotEvaluated)]
    public async Task ComparesSubmittedDtdWithPublishedChecksum(string variant, bool changed, bool missing, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            capturedName: changed ? IchDtd : null, omittedTargetFile: missing ? IchDtd : null);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("1d6f631cc6b6357f0f4fe378e5f79a27", finding.Expected);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.MD5.HashData("captured content"u8)).ToLowerInvariant(), finding.Actual);
            Assert.Equal("0001/" + IchDtd, Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(check.Findings);
        var report = result.CreateReport(PackageValidationMode.Development);
        var coverage = Assert.Single(report.Coverage, row => row.RuleId == check.RuleId);
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected, coverage.CheckStatus);
    }

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
