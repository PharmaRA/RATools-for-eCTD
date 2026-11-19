using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaExtraneousHeadingAttributeRuleTests
{
    [Theory]
    [InlineData("m4-nonclinical-study-reports", "manufacturer", "Acme", CheckStatus.Fail)]
    [InlineData("m5-clinical-study-reports", "manufacturer", "", CheckStatus.Fail)]
    [InlineData("m5-clinical-study-reports", "species", "human", CheckStatus.Fail)]
    [InlineData("m4-nonclinical-study-reports", "ID", "section", CheckStatus.Pass)]
    [InlineData("m5-clinical-study-reports", "xml:lang", "en", CheckStatus.Pass)]
    [InlineData("m5-clinical-study-reports", "xmlns:extra", "urn:extra", CheckStatus.Pass)]
    [InlineData("m5-clinical-study-reports", "extra:manufacturer", "Acme", CheckStatus.Fail)]
    public async Task DistinguishesAllowedAndExtraneousAttributes(string name, string attribute, string value, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Root!.Add(Heading(name, attribute, value)));
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("@" + attribute, finding.Location.FieldPath);
            Assert.Equal(value, finding.Actual);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.True(finding.Location.Line > 0);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else
        {
            Assert.Empty(check.Findings);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
                item => item.RuleId == "FDA47-1368").CheckStatus);
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
    public async Task RequiresBoundEvidenceAndPreservesKnownFailures(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml => xml.Root!.Add(Heading("m5-clinical-study-reports", "manufacturer", "Acme")));
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("wrong-parent")]
    [InlineData("regional")]
    public async Task OutOfScopeHeadingsDoNotCreateOfficialFindings(string variant)
    {
        void Edit(XDocument xml)
        {
            var heading = Heading("m5-clinical-study-reports", "manufacturer", "Acme");
            if (variant == "namespace") heading.Name = XName.Get(heading.Name.LocalName, "urn:other");
            xml.Root!.Add(variant == "wrong-parent" ? new XElement("m4-nonclinical-study-reports", heading) : heading);
        }
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: variant == "regional" ? null : Edit, editRegional: variant == "regional" ? Edit : null);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368");
        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task NestedQualifiedHeadingIsChecked()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Root!.Add(new XElement("m5-clinical-study-reports",
                Heading("m5-3-clinical-study-reports", "manufacturer", "Acme"))));
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368").Status);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaCoverage()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "ich-profile");
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1368");
    }

    private static XElement Heading(string name, string attribute, string value)
    {
        var heading = new XElement(name);
        if (attribute == "xml:lang") heading.SetAttributeValue(XNamespace.Xml + "lang", value);
        else if (attribute == "xmlns:extra") heading.SetAttributeValue(XNamespace.Xmlns + "extra", value);
        else if (attribute == "extra:manufacturer")
        {
            heading.SetAttributeValue(XNamespace.Xmlns + "extra", "urn:extra");
            heading.SetAttributeValue(XName.Get("manufacturer", "urn:extra"), value);
        }
        else heading.SetAttributeValue(attribute, value);
        return heading;
    }
}
