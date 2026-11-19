using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredHeadingAttributeRuleTests
{
    [Theory]
    [InlineData("m2s", "manufacturer", null, CheckStatus.Fail)]
    [InlineData("m2s", "substance", "", CheckStatus.Fail)]
    [InlineData("m3s", "manufacturer", " ", CheckStatus.Fail)]
    [InlineData("m3s", "substance", null, CheckStatus.Fail)]
    [InlineData("m2e", "indication", null, CheckStatus.Fail)]
    [InlineData("m5e", "indication", "", CheckStatus.Fail)]
    [InlineData("m2s", "manufacturer", "Valid", CheckStatus.Pass)]
    [InlineData("m3s", "substance", "Valid", CheckStatus.Pass)]
    [InlineData("m2e", "indication", "Valid", CheckStatus.Pass)]
    [InlineData("m5e", "indication", "Valid", CheckStatus.Pass)]
    public async Task RequiredHeadingAttributesAreLocated(string section, string attribute, string? value, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => AddHeading(xml, section, attribute, value));
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1357");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("@" + attribute, finding.Location.FieldPath);
            Assert.Equal(value, finding.Actual);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.True(finding.Location.Line > 0);
            Assert.Contains(result.Lifecycle.XmlInspections.SelectMany(item => item.Checks).SelectMany(item => item.Findings),
                item => item.Code == "MISSING_REQUIRED_NODE_ATTRIBUTE" && item.Location.FieldPath == "@" + attribute);
        }
        else
        {
            Assert.Empty(check.Findings);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
                item => item.RuleId == "FDA47-1357").CheckStatus);
        }
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-19", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-19", CheckStatus.NotEvaluated)]
    public async Task SourceDateCompletenessAndBudgetStayExplicit(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml => AddHeading(xml, "m5e", "indication", null));
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1357").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("wrong-parent")]
    [InlineData("regional")]
    public async Task SimilarNamesOutsideTheIchHeadingScopeDoNotCreateFindings(string variant)
    {
        void Edit(XDocument xml)
        {
            var heading = AddHeading(xml, "m2e", "indication", null);
            if (variant == "namespace") heading.Name = XName.Get(heading.Name.LocalName, "urn:other");
            if (variant == "wrong-parent") { heading.Remove(); xml.Root!.Add(heading); }
        }
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: variant == "regional" ? null : Edit, editRegional: variant == "regional" ? Edit : null);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1357");
        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task UnreadRegionalEvidenceCannotEstablishPass()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "unread");
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1357").Status);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaCoverage()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "ich-profile");
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1357");
    }

    private static XElement AddHeading(XDocument xml, string section, string attribute, string? value)
    {
        string[] chain = section switch
        {
            "m2s" => ["m2-common-technical-document-summaries", "m2-3-quality-overall-summary", "m2-3-s-drug-substance"],
            "m3s" => ["m3-quality", "m3-2-body-of-data", "m3-2-s-drug-substance"],
            "m2e" => ["m2-common-technical-document-summaries", "m2-7-clinical-summary", "m2-7-3-summary-of-clinical-efficacy"],
            "m5e" => ["m5-clinical-study-reports", "m5-3-clinical-study-reports", "m5-3-5-reports-of-efficacy-and-safety-studies"],
            _ => throw new ArgumentException("Unknown section")
        };
        var parent = xml.Root!;
        foreach (var name in chain) { var child = new XElement(name); parent.Add(child); parent = child; }
        if (section.EndsWith('s')) { parent.SetAttributeValue("substance", "Valid"); parent.SetAttributeValue("manufacturer", "Valid"); }
        else parent.SetAttributeValue("indication", "Valid");
        parent.SetAttributeValue(attribute, value);
        return parent;
    }
}
