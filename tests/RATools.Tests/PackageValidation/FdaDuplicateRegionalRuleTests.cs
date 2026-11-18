using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaDuplicateRegionalRuleTests
{
    [Theory]
    [InlineData("valid", null, "2013-04-30", CheckStatus.Pass)]
    [InlineData("valid", "copies/us-regional.xml", "2026-11-18", CheckStatus.Fail)]
    [InlineData("duplicate-region", null, "2026-11-18", CheckStatus.Fail)]
    [InlineData("nested-region", null, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("self-region", null, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("delete-region", null, "2026-11-18", CheckStatus.Pass)]
    [InlineData("truncated", null, "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "copies/us-regional.xml", "2026-11-18", CheckStatus.Fail)]
    [InlineData("history-1111", null, "2026-11-18", CheckStatus.Pass)]
    [InlineData("unread", null, "2026-11-18", CheckStatus.Pass)]
    [InlineData("budget", "copies/us-regional.xml", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("valid", null, "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", null, null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", null, "2026-11-18", CheckStatus.NotEvaluated)]
    public async Task CountsPhysicalAndReferencedBackbonesIndependently(string variant, string? file, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date, capturedName: file);
        var check = Assert.Single(result.Files.Checks, row => row.RuleId == "FDA47-1111");
        if (variant == "self-region")
        {
            var xml = Assert.Single(result.Lifecycle.XmlInspections);
            Assert.Single(xml.Backbones, backbone => backbone.LogicalPath == "0001/index.xml");
            Assert.Contains(xml.Checks.SelectMany(row => row.Findings), finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE" &&
                finding.Actual == "index.xml" && finding.Location.LeafId == "regional-2");
        }
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            if (file is not null) Assert.Contains(finding.Location.Files!, item => item.LogicalPath == "0001/" + file);
            else Assert.Equal("regional-2", finding.Location.LeafId);
        }
        else Assert.Empty(check.Findings);
    }
}
