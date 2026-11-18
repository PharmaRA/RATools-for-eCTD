using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFileExtensionRuleTests
{
    private const string M2 = "m2-common-technical-document-summaries";
    private const string M3 = "m3-quality";
    private const string M5 = "m5-clinical-study-reports";

    [Theory]
    [InlineData(M2, "same.txt", "same.txt", "valid", "2025-08-20", CheckStatus.Fail)]
    [InlineData(M3, "same.txt", "same.txt", "valid", "2025-08-20", CheckStatus.Pass)]
    [InlineData(M3, "same.sdf", "same.sdf", "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData(M5, "same.sdf#anchor", "same.sdf", "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData(M2, "same.pdf", "same.pdf", "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData(M2, "same.docx", "same.docx", "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(M2, "missing.txt", null, "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(M2, "same.txt", "same.txt", "truncated", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(M2, "same.txt", "same.txt", "missing-format-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(M2, "same.txt", "same.txt", "missing-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData(M2, "same.txt", "same.txt", "valid", null, CheckStatus.NotEvaluated)]
    [InlineData(M2, "same.txt", "same.txt", "valid", "2025-08-19", CheckStatus.NotEvaluated)]
    [InlineData(M2, "same.txt", "same.txt", "valid", "2008-03-09", CheckStatus.NotApplicable)]
    public async Task UsesCapturedLeafSectionAndApplicableFormatRevision(string module, string href, string? name, string variant, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", href, null, variant, date, module, name);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1255");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Contains(module, finding.Location.NodePath);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0001/" + name);
        }
        else Assert.Empty(check.Findings);
        var coverage = Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1255");
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected, coverage.CheckStatus);
    }
}
