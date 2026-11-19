using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfAttributeWhitespaceTests
{
    [Theory]
    [InlineData(" leading", " leading", CheckStatus.Fail)]
    [InlineData("trailing ", "trailing ", CheckStatus.Fail)]
    [InlineData(" ", " ", CheckStatus.Fail)]
    [InlineData("&#x20;value&#32;", " value ", CheckStatus.Fail)]
    [InlineData("\tvalue\t", " value ", CheckStatus.Fail)]
    [InlineData("&#9;value&#9;", "\tvalue\t", CheckStatus.Pass)]
    [InlineData("internal space", "internal space", CheckStatus.Pass)]
    [InlineData("", "", CheckStatus.Pass)]
    public async Task ExplicitStfAttributesUseXmlDecodedPreNormalizationValues(string written, string decoded, CheckStatus expected)
    {
        var result = await Inspect(written);
        var stf = Assert.Single(TargetXml(result).StudyTaggingFiles.Documents);
        var category = Assert.Single(stf.Elements, element => element.LocalName == "category");
        Assert.Equal(decoded, category.Attributes["name"].ExplicitValue);
        var check = Check(result);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("0001/m4/study/stf-a.xml", finding.Location.LogicalPath);
            Assert.Equal("@name", finding.Location.FieldPath);
            Assert.Equal(category.NodePath, finding.Location.NodePath);
            Assert.Null(finding.Location.LeafId);
            Assert.Equal(decoded, finding.Actual);
            Assert.True(finding.Location.Line > 0);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal(stf.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
        }
    }

    [Fact]
    public async Task StfDefaultsAreNotExplicitAndDoNotUpgradeWholeRuleCoverage()
    {
        var result = await Inspect("study-type");
        var stf = Assert.Single(TargetXml(result).StudyTaggingFiles.Documents);
        var attribute = stf.Elements[0].Attributes["dtd-version"];
        Assert.True(attribute.IsDefault);
        Assert.Null(attribute.ExplicitValue);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
            item => item.RuleId == "FDA47-1344").CheckStatus);
    }

    [Theory]
    [InlineData("truncated-stf")]
    [InlineData("missing-explicit-namespace")]
    [InlineData("missing-stf")]
    public async Task IncompleteStfEvidenceNeverPasses(string variant)
    {
        var result = await Inspect("study-type", variant);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
    }

    [Theory]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task StfChecksRetainSourceDateAndFindingBudget(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect(" spaced ", variant, date);
        Assert.Equal(expected, Check(result).Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    [Fact]
    public async Task KnownBackboneFailureSurvivesIncompleteStf()
    {
        var result = await Inspect("study-type", "truncated-stf", backboneFailure: true);
        var finding = Assert.Single(Check(result).Findings);
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
    }

    [Fact]
    public async Task HistoricalStfWhitespaceIsNotATargetFinding()
    {
        var result = await Inspect("study-type", "history-stf");
        Assert.Contains(result.Lifecycle.XmlInspections, xml => xml.Backbones.Any(backbone => backbone.LogicalPath.StartsWith("0000/", StringComparison.Ordinal)) &&
            xml.StudyTaggingFiles.Documents.Any(document => document.LogicalPath == "0000/m4/study/stf-old.xml"));
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344");
    private static PackageXmlInspection TargetXml(PackageDeliveryInspection result) => Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Lifecycle.InputDigest);

    private static string Xml(string value) => """
        <!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>
        <ectd:study xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink">
        <study-identifier><title>A</title><study-id>A</study-id><category name="VALUE" info-type="ich">toxicity</category></study-identifier>
        <study-document/>
        </ectd:study>
        """.Replace("VALUE", value, StringComparison.Ordinal);

    private static Task<PackageDeliveryInspection> Inspect(string value, string variant = "valid", string? date = "2026-11-20", bool backboneFailure = false)
    {
        var xml = Xml(value);
        if (variant == "truncated-stf") xml = xml[..^12];
        if (variant == "missing-explicit-namespace") xml = xml.Replace(" xmlns:ectd=\"http://www.ich.org/ectd\"", "", StringComparison.Ordinal);
        return FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            capturedName: variant == "missing-stf" ? null : "m4/study/stf-a.xml", capturedBytes: Encoding.UTF8.GetBytes(xml),
            editIndex: index =>
            {
                if (backboneFailure) index.Root!.SetAttributeValue(XNamespace.Xml + "lang", " en ");
                index.Root!.Add(new XElement("m4-nonclinical-study-reports", new XElement("m4-3-literature-references",
                    new XElement("leaf", new XAttribute("ID", "stf"), new XAttribute("operation", "new"),
                        new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml"), new XElement("title", "Study")))));
            },
            editSource: variant != "history-stf" ? null : root =>
            {
                var path = Path.Combine(root, "0000/m4/study/stf-old.xml");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Xml(" old "));
            });
    }
}
