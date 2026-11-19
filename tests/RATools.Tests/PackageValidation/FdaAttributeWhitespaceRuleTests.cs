using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaAttributeWhitespaceRuleTests
{
    [Fact]
    public async Task NonLeafAttributeAndKnownFailureSurviveUnreadRegionalBackbone()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "unread",
            editIndex: xml => xml.Root!.SetAttributeValue(XNamespace.Xml + "lang", " en "));
        var finding = Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344").Findings);
        Assert.Equal(CheckStatus.Fail, finding.CheckStatus);
        Assert.Equal("@xml:lang", finding.Location.FieldPath);
        Assert.Null(finding.Location.LeafId);
    }

    [Fact]
    public async Task DtdDefaultIsNotAnExplicitValueAndDoesNotEstablishFullCoverage()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Root!.Attribute("dtd-version")!.Remove());
        var backbone = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.Backbones), item => item.LogicalPath == "0001/index.xml");
        var attribute = backbone.Elements[0].Attributes["dtd-version"];
        Assert.True(attribute.IsDefault);
        Assert.Null(attribute.ExplicitValue);
        Assert.Equal("3.2", attribute.Value);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344").Status);
        var report = result.CreateReport(PackageValidationMode.Development);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(report.Coverage, item => item.RuleId == "FDA47-1344").CheckStatus);
        Assert.False(report.IsReadyForFinalization);
    }

    [Fact]
    public async Task MissingExplicitNamespaceEvidenceCannotPass()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndexText: xml => xml.Replace(" xmlns:ectd=\"http://www.ich.org/ectd\"", "", StringComparison.Ordinal));
        var backbone = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.Backbones), item => item.LogicalPath == "0001/index.xml");
        Assert.True(backbone.ReadComplete);
        Assert.True(backbone.Elements[0].Attributes["xmlns:ectd"].IsDefault);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344").Status);
    }

    [Fact]
    public async Task AlternatePrefixIsPreservedInLocation()
    {
        XNamespace xlink = "http://www.w3c.org/1999/xlink";
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single();
                leaf.SetAttributeValue(XNamespace.Xmlns + "link", xlink.NamespaceName);
                leaf.SetAttributeValue(xlink + "role", " role ");
            });
        var finding = Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344").Findings);
        Assert.Equal("@link:role", finding.Location.FieldPath);
        Assert.Equal(" role ", finding.Actual);
    }

    [Theory]
    [InlineData("ID", " regional ", "regional")]
    [InlineData("operation", " new ", "new")]
    [InlineData("keywords", " text ", " text ")]
    public async Task PreservesExplicitValueBeforeDtdNormalization(string name, string written, string normalized)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Descendants("leaf").Single().SetAttributeValue(name, written));
        var backbone = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.Backbones), item => item.LogicalPath == "0001/index.xml");
        var leaf = Assert.Single(backbone.Elements, item => item.LocalName == "leaf");
        var attribute = leaf.Attributes[name];
        Assert.False(attribute.IsDefault);
        Assert.Equal(normalized, attribute.Value);
        Assert.Equal(written, attribute.ExplicitValue);
        var defaultAttribute = leaf.Attributes["xlink:type"];
        Assert.True(defaultAttribute.IsDefault);
        Assert.Equal("simple", defaultAttribute.Value);
        Assert.Null(defaultAttribute.ExplicitValue);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344");
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("@" + name, finding.Location.FieldPath);
        Assert.Equal(written, finding.Actual);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.True(finding.Location.Line > 0);
        Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
    }

    [Theory]
    [InlineData(" plain", CheckStatus.Fail)]
    [InlineData("plain ", CheckStatus.Fail)]
    [InlineData(" ", CheckStatus.Fail)]
    [InlineData("plain text", CheckStatus.Pass)]
    [InlineData("", CheckStatus.Pass)]
    [InlineData("&#x20;plain&#32;", CheckStatus.Fail)]
    [InlineData("&quot;plain&quot;", CheckStatus.Pass)]
    [InlineData("&#9;plain&#9;", CheckStatus.Pass)]
    [InlineData("\tplain\t", CheckStatus.Fail)]
    public async Task ChecksXmlDecodedExplicitValues(string value, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Descendants("leaf").Single().SetAttributeValue("keywords", "MARKER"),
            editIndexText: xml => xml.Replace("keywords=\"MARKER\"", "keywords=\"" + value + "\"", StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344").Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-19", CheckStatus.NotEvaluated)]
    public async Task RegionalAttributesRespectEvidenceAndLimits(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editRegional: xml => xml.Descendants("form").First().SetAttributeValue("form-type", " fdaft1 "));
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal("0001/m1/us/us-regional.xml", Assert.Single(check.Findings).Location.LogicalPath);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Fact]
    public async Task HistoricalWhitespaceDoesNotBecomeATargetFinding()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "history-1344");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1344");
        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("ich-profile")]
    [InlineData("old-catalog")]
    public async Task RequiresSelectedCatalogRule(string variant)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant);
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1344");
    }
}
