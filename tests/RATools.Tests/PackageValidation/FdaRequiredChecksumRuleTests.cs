using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredChecksumRuleTests
{
    [Theory]
    [InlineData("new", null, CheckStatus.Fail)]
    [InlineData("new", "", CheckStatus.Fail)]
    [InlineData("append", " ", CheckStatus.Fail)]
    [InlineData("replace", "\t", CheckStatus.Fail)]
    [InlineData("new", "not-a-digest", CheckStatus.Pass)]
    [InlineData("append", "abc", CheckStatus.Pass)]
    [InlineData("replace", "abc", CheckStatus.Pass)]
    [InlineData("delete", null, CheckStatus.Pass)]
    [InlineData("delete", "", CheckStatus.Pass)]
    [InlineData("delete", "abc", CheckStatus.Pass)]
    [InlineData("unknown", null, CheckStatus.NotEvaluated)]
    [InlineData(null, null, CheckStatus.NotEvaluated)]
    public async Task RequiresValuesOnlyForExplicitContentOperations(string? operation, string? checksum, CheckStatus expected)
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(checksum: checksum, operation: operation);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1425");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) FdaChecksumTypeRuleTests.AssertFinding(Assert.Single(check.Findings), "@checksum", checksum, "0001/index.xml", "regional");
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("missing-ich-spec", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-19", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-19", CheckStatus.NotEvaluated)]
    public async Task RequiresBoundCompleteEvidence(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(checksum: null, variant: variant, date: date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1425").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    public async Task RegionalChecksumHasItsOwnLocation(string variant, CheckStatus expected)
    {
        var result = await FdaChecksumTypeRuleTests.Inspect(checksum: null, variant: variant, regional: true);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1425");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) FdaChecksumTypeRuleTests.AssertFinding(Assert.Single(check.Findings), "@checksum", null, "0001/m1/us/us-regional.xml", "checksum-leaf");
    }

    [Fact]
    public async Task NamespacedLookalikeCannotSupplyChecksum()
        => Assert.Equal(CheckStatus.Fail, Assert.Single((await FdaChecksumTypeRuleTests.Inspect(checksum: "abc", foreignChecksum: true)).Files.Checks, item => item.RuleId == "FDA47-1425").Status);

    [Fact]
    public async Task HistoricalMissingChecksumIsNotTargetEvidence()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "history-1425",
            editIndex: xml => { var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", "abc"); leaf.SetAttributeValue("checksum-type", "md5"); },
            editSource: source =>
            {
                var path = Path.Combine(source, "0000/index.xml"); var xml = System.Xml.Linq.XDocument.Load(path);
                var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("operation", "new"); leaf.SetAttributeValue("checksum", null); xml.Save(path);
            });
        Assert.Contains(result.Lifecycle.XmlInspections.SelectMany(item => item.Checks).SelectMany(item => item.Findings),
            item => item.Code == "DTD_VALIDATION_ERROR" && item.Location.SequenceNumber == "0000");
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1425").Status);
    }

    [Fact]
    public async Task PartialDoesNotPromoteCoverage()
    {
        var result = await FdaChecksumTypeRuleTests.Inspect();
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1425").CheckStatus);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await FdaChecksumTypeRuleTests.Inspect(variant: "ich-profile")).Checks, item => item.RuleId == "FDA47-1425");
}
