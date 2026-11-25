using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFileExtensionAdditionalRowsTests
{
    public static IEnumerable<object[]> Rows()
    {
        foreach (var extension in new[] { "svg", "rmd", "json", "cas", "dat" })
        foreach (var module in new[] { 2, 3, 4, 5 })
            yield return [extension, module, (extension == "svg" ? module >= 3 : module == 5) ? CheckStatus.Pass : CheckStatus.Fail];
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task AdditionalUnconditionalFormatRowsFollowTheActualModule(string extension, int module, CheckStatus expected)
    {
        var heading = Heading(module); var name = "same." + extension;
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", name, null, date: "2025-08-20", contentModule: heading, capturedName: name);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1255");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Contains(heading, finding.Location.NodePath);
            Assert.Equal(name, finding.Actual);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0001/" + name);
        }
        else Assert.Empty(check.Findings);
        var coverage = Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1255");
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected, coverage.CheckStatus);
    }

    [Theory]
    [InlineData("same.JSON", "same.JSON", "valid", "2025-08-20", 3, CheckStatus.Fail)]
    [InlineData("same.%6ason", "same.json", "valid", "2025-08-20", 3, CheckStatus.Fail)]
    [InlineData("same.json#part", "same.json", "valid", "2025-08-20", 3, CheckStatus.Fail)]
    [InlineData("same.json?query", "same.json", "valid", "2025-08-20", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", null, "valid", "2025-08-20", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", "same.json", "missing-format-source", "2025-08-20", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", "same.json", "missing-source", "2025-08-20", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", "same.json", "truncated", "2025-08-20", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", "same.json", "budget", "2025-08-20", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", "same.json", "valid", "2025-08-19", 3, CheckStatus.NotEvaluated)]
    [InlineData("same.json", "same.json", "valid", null, 3, CheckStatus.NotEvaluated)]
    [InlineData("m5/same.json", "m5/same.json", "valid", "2025-08-20", 3, CheckStatus.Fail)]
    [InlineData("m2/same.json", "m2/same.json", "valid", "2025-08-20", 5, CheckStatus.Pass)]
    [InlineData("same.docx", "same.docx", "valid", "2025-08-20", 2, CheckStatus.NotEvaluated)]
    [InlineData("same.csv", "same.csv", "valid", "2025-08-20", 5, CheckStatus.NotEvaluated)]
    [InlineData("same.zip", "same.zip", "valid", "2025-08-20", 5, CheckStatus.NotEvaluated)]
    [InlineData("same.svg", "same.svg", "valid", "2025-08-20", 3, CheckStatus.Pass)]
    public async Task NewRowsRetainBindingAndUnqualifiedPolicyBoundaries(string href, string? captured, string variant, string? date, int module, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", href, null, variant, date, Heading(module), captured);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1255");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
        // A permitted row is not proof of full regulatory coverage or of an SVG archive copy.
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected,
            Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1255").CheckStatus);
    }

    private static string Heading(int module) => module switch
    {
        2 => "m2-common-technical-document-summaries", 3 => "m3-quality",
        4 => "m4-nonclinical-study-reports", 5 => "m5-clinical-study-reports", _ => throw new ArgumentOutOfRangeException(nameof(module))
    };
}
