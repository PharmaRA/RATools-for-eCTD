using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaUtilityFileRuleTests
{
    [Theory]
    [InlineData(null, false, "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("util/style/.DS_Store", true, "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData("util/style/.DS_Store", false, "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("util/dtd/extra.mod", false, "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("util/dtd/eu-envelope.mod", false, "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("util/other/extra.xml", false, "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("m3/.DS_Store", true, "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData(null, false, "missing-qa-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(null, false, "missing-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(null, false, "valid", null, CheckStatus.NotEvaluated)]
    [InlineData(null, false, "valid", "2008-03-09", CheckStatus.NotApplicable)]
    public async Task AssessesCapturedUtilityFilesAgainstArchivedEvidence(string? file, bool finderHeader, string variant, string? date, CheckStatus expected)
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            capturedName: file, capturedBytes: finderHeader ? [0, 0, 0, 1, 66, 117, 100, 49] : null);
        var check = Assert.Single(inspection.Files.Checks, row => row.RuleId == "FDA47-1314");
        Assert.Equal(expected, check.Status);
        if (file?.StartsWith("util/", StringComparison.Ordinal) == true)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(expected, finding.CheckStatus);
            Assert.Equal("0001/" + file, Assert.Single(finding.Location.Files!).LogicalPath);
        }
        var coverage = Assert.Single(inspection.CreateReport(PackageValidationMode.Development).Coverage, row => row.RuleId == "FDA47-1314");
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected, coverage.CheckStatus);
    }
}
