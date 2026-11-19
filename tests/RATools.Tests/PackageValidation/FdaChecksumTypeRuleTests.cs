using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaChecksumTypeRuleTests
{
    [Theory]
    [InlineData("md5", "new", CheckStatus.Pass)]
    [InlineData("MD5", "new", CheckStatus.Pass)]
    [InlineData("Md5", "append", CheckStatus.Pass)]
    [InlineData("sha256", "new", CheckStatus.Fail)]
    [InlineData("md5 ", "replace", CheckStatus.Fail)]
    [InlineData("", "new", CheckStatus.Fail)]
    [InlineData(" ", "new", CheckStatus.Fail)]
    [InlineData(null, "new", CheckStatus.NotEvaluated)]
    [InlineData("md5", "delete", CheckStatus.Pass)]
    [InlineData("sha1", "delete", CheckStatus.Fail)]
    [InlineData("sha1", "unknown", CheckStatus.Fail)]
    public async Task ChecksTheSuppliedAlgorithmWithoutChangingItsValue(string? type, string operation, CheckStatus expected)
    {
        var result = await Inspect(type, operation: operation);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1408");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) AssertFinding(Assert.Single(check.Findings), "@checksum-type", type, "0001/index.xml", "regional");
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
    public async Task EvidenceAndBudgetRemainExplicit(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("sha256", variant: variant, date: date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1408").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    public async Task RegionalAttributesAreCheckedOnlyWhenRead(string variant, CheckStatus expected)
    {
        var result = await Inspect("sha256", variant: variant, regional: true);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1408");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) AssertFinding(Assert.Single(check.Findings), "@checksum-type", "sha256", "0001/m1/us/us-regional.xml", "checksum-leaf");
    }

    [Fact]
    public async Task UnrelatedNamespaceDoesNotSupplyTheAlgorithm()
        => Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Inspect("sha256", foreignAttribute: true)).Files.Checks, item => item.RuleId == "FDA47-1408").Status);

    [Fact]
    public async Task HistoryIsNotReportedAsTargetDefects()
        => Assert.Equal(CheckStatus.Pass, Assert.Single((await Inspect("md5", variant: "history-checksum")).Files.Checks, item => item.RuleId == "FDA47-1408").Status);

    [Fact]
    public async Task IncompleteRegionalCoverageCannotPass()
        => Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Inspect("md5", variant: "unread")).Files.Checks, item => item.RuleId == "FDA47-1408").Status);

    [Theory]
    [InlineData("md5", CheckStatus.Pass)]
    [InlineData("MD5", CheckStatus.Pass)]
    [InlineData("sha256", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task CompletedRuleRetainsActualCoverageStatus(string? type, CheckStatus expected)
    {
        var result = await Inspect(type);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1408").CheckStatus);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaCoverage()
        => Assert.DoesNotContain((await Inspect("sha256", variant: "ich-profile")).Checks, item => item.RuleId == "FDA47-1408");

    internal static void AssertFinding(ValidationFinding finding, string field, string? actual, string path, string? leafId)
    {
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Equal(field, finding.Location.FieldPath);
        Assert.Equal(actual, finding.Actual);
        Assert.Equal(path, Assert.Single(finding.Location.Files!).LogicalPath);
        Assert.Equal(leafId, finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
    }

    internal static Task<PackageDeliveryInspection> Inspect(string? type = "md5", string? checksum = "abc", string? operation = "new",
        string variant = "valid", string? date = "2026-11-19", bool regional = false, bool foreignAttribute = false, bool foreignChecksum = false)
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single();
                leaf.SetAttributeValue("checksum-type", regional ? "md5" : type);
                leaf.SetAttributeValue("checksum", regional ? "abc" : checksum);
                if (!regional)
                {
                    leaf.SetAttributeValue("operation", operation);
                    if (foreignAttribute) { leaf.SetAttributeValue("checksum-type", null); leaf.SetAttributeValue(XName.Get("checksum-type", "urn:other"), type); }
                    if (foreignChecksum) { leaf.SetAttributeValue("checksum", null); leaf.SetAttributeValue(XName.Get("checksum", "urn:other"), checksum); }
                }
            },
            editRegional: regional ? xml =>
            {
                var leaf = new XElement("leaf", new XAttribute("ID", "checksum-leaf"), new XElement("title", "Form"));
                leaf.SetAttributeValue("operation", operation); leaf.SetAttributeValue("checksum-type", type); leaf.SetAttributeValue("checksum", checksum);
                xml.Descendants("form").First().Add(leaf);
            } : null,
            editSource: variant == "history-checksum" ? source =>
            {
                var path = Path.Combine(source, "0000/index.xml"); var xml = XDocument.Load(path);
                var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum-type", "sha1"); leaf.SetAttributeValue("checksum", " ");
                leaf.SetAttributeValue("operation", "delete"); xml.Save(path);
            } : null);
}
