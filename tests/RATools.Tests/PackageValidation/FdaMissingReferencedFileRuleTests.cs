using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaMissingReferencedFileRuleTests
{
    [Fact]
    public async Task ExtraneousDeletePathIsReportedBy1051()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("delete", "m2/absent.txt", null,
            contentModule: "m2-common-technical-document-summaries");
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1051").Status);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1323").Status);
    }

    [Fact]
    public async Task HistoricalMissingFilesAreNotTargetFindings()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "history-1323-missing");
        Assert.Contains(result.Files.Checks.SelectMany(item => item.Findings), finding =>
            finding.Code == "REFERENCED_FILE_MISSING" && finding.Location.SequenceNumber == "0000");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1323");
        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("new", "m2/absent.txt", "valid", null, CheckStatus.Fail)]
    [InlineData("new", "m2/content.txt", "valid", "m2/content.txt", CheckStatus.Pass)]
    [InlineData("new", "m2/Content.txt", "valid", "m2/content.txt", CheckStatus.Fail)]
    [InlineData("new", "m2/content%2Etxt#part", "valid", "m2/content.txt", CheckStatus.Pass)]
    [InlineData("new", "../0000/index.xml", "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "../0000/index.xml", "history-1323", null, CheckStatus.Pass)]
    [InlineData("new", "../0000/m2/absent.txt", "history-1323", null, CheckStatus.Fail)]
    [InlineData("new", "../0002/index.xml", "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "https://example.invalid/file.txt", "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "m2/file%ZZ.txt", "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "m2/content.txt?download", "valid", "m2/content.txt", CheckStatus.NotEvaluated)]
    [InlineData("new", null, "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("delete", null, "valid", null, CheckStatus.Pass)]
    [InlineData("new", "m2/absent.txt", "truncated", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "m2/absent.txt", "budget", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "m2/absent.txt", "missing-source", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "m2/absent.txt", "missing-qa-source", null, CheckStatus.NotEvaluated)]
    [InlineData("new", "m2/absent.txt", "alias", null, CheckStatus.Fail)]
    public async Task SeparatesMissingFilesFromUnavailableEvidence(string operation, string? href, string variant, string? capturedName, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect(operation, href, null, variant,
            contentModule: "m2-common-technical-document-summaries", capturedName: capturedName);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1323");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("regional", finding.Location.LeafId);
            Assert.Equal(variant == "alias" ? "@link:href" : "@xlink:href", finding.Location.FieldPath);
            Assert.Equal(href, finding.Actual);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.True(finding.Location.Line > 0);
        }
        else Assert.Empty(check.Findings);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData("valid", "missing.txt", CheckStatus.Fail)]
    [InlineData("valid", "us-regional.xml#part", CheckStatus.Pass)]
    [InlineData("unread", "missing.txt", CheckStatus.NotEvaluated)]
    [InlineData("wrong-regional-href-namespace", "missing.txt", CheckStatus.NotEvaluated)]
    public async Task UsesTheRegionalBackboneAsTheRelativeBase(string variant, string href, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, regionalContentHref: href);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1323");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("form-leaf", finding.Location.LeafId);
            Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        }
    }

    [Theory]
    [InlineData("2022-02-28", CheckStatus.NotApplicable)]
    [InlineData("2022-03-01", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task RequiresAnApplicableSelectionDate(string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m2/absent.txt", null, date: date,
            contentModule: "m2-common-technical-document-summaries");
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1323").Status);
    }

    [Fact]
    public async Task DoesNotAddFdaCoverageToTheIchProfile()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m2/absent.txt", null, "ich-profile",
            contentModule: "m2-common-technical-document-summaries");
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1323");
    }
}
