using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaTextLengthRuleTests
{
    [Theory]
    [InlineData(512, CheckStatus.NotEvaluated)]
    [InlineData(513, CheckStatus.Fail)]
    public async Task UnclosedTitleUsesOnlyTheActuallyRetainedText(int length, CheckStatus expected)
    {
        var value = new string('A', length);
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Descendants("leaf").Single().Element("title")!.Value = value,
            editIndexText: xml => xml[..(xml.IndexOf(value, StringComparison.Ordinal) + value.Length)] + "<");
        var index = Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).Backbones, item => item.LogicalPath == "0001/index.xml");
        Assert.False(index.ReadComplete);
        Assert.Equal(value, Assert.Single(index.Elements, item => item.LocalName == "title").Text);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("unicode", CheckStatus.NotEvaluated)]
    [InlineData("combining", CheckStatus.NotEvaluated)]
    [InlineData("tab", CheckStatus.NotEvaluated)]
    [InlineData("ascii-space", CheckStatus.Fail)]
    [InlineData("xml-entity", CheckStatus.Fail)]
    [InlineData("nested-title", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-title", CheckStatus.NotEvaluated)]
    [InlineData("foreign-title", CheckStatus.NotEvaluated)]
    [InlineData("extension-title", CheckStatus.NotEvaluated)]
    [InlineData("keywords-unsplit", CheckStatus.Fail)]
    [InlineData("foreign-keywords", CheckStatus.NotEvaluated)]
    [InlineData("after-root-truncation", CheckStatus.Fail)]
    [InlineData("after-root-short", CheckStatus.NotEvaluated)]
    public async Task ExactTextBoundariesAreNotTrimmedOrGuessed(string scenario, CheckStatus expected)
    {
        var text = scenario switch
        {
            "unicode" => new string('文', 513), "combining" => string.Concat(Enumerable.Repeat("e\u0301", 300)),
            "tab" => new string('A', 512) + "\t", "ascii-space" => new string('A', 512) + " ",
            "after-root-short" => new string('A', 512), _ => new string('A', 513)
        };
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, editIndex: xml =>
        {
            var leaf = xml.Descendants("leaf").Single(); var title = leaf.Element("title")!;
            if (scenario == "keywords-unsplit") leaf.SetAttributeValue("keywords", string.Join(' ', Enumerable.Repeat("word", 110)));
            else if (scenario == "foreign-keywords") leaf.SetAttributeValue(XName.Get("keywords", "urn:other"), text);
            else if (scenario == "extension-title") xml.Root!.Add(new XElement("m2-common-technical-document-summaries",
                new XElement("m2-5-clinical-overview", new XElement("node-extension", new XElement("title", text)))));
            else
            {
                title.Value = text;
                if (scenario == "nested-title") title.Add(new XElement("span", "nested"));
                if (scenario == "duplicate-title") leaf.Add(new XElement("title", text));
                if (scenario == "foreign-title") title.Name = XName.Get("title", "urn:other");
            }
        }, editIndexText: scenario == "xml-entity" ? xml => xml.Replace(new string('A', 513), string.Concat(Enumerable.Repeat("&#65;", 513)), StringComparison.Ordinal) :
            scenario.StartsWith("after-root", StringComparison.Ordinal) ? xml => xml + "<broken" : null);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
        Assert.NotEqual(CheckStatus.Pass, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1500").CheckStatus);
    }

    [Theory]
    [InlineData("valid", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", "2013-04-30", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("missing-ich-spec", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-26", CheckStatus.Fail)]
    public async Task SourceDateAndSharedFindingBudgetRemainRequired(string variant, string? date, CheckStatus expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml => xml.Descendants("leaf").Single().Element("title")!.Value = new string('A', 513));
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Fact]
    public async Task RegionalLeafTitleIsLocatedWithoutBorrowingTheIndexTitle()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editRegional: xml => xml.Descendants("form").First().Add(new XElement("leaf", new XAttribute("ID", "regional-content"),
                new XAttribute("operation", "new"), new XElement("title", new string('A', 513)))));
        var finding = Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500").Findings);
        Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        Assert.Equal("regional-content", finding.Location.LeafId);
    }

    [Theory]
    [InlineData("title", 512, CheckStatus.NotEvaluated)]
    [InlineData("title", 513, CheckStatus.Fail)]
    [InlineData("keywords", 512, CheckStatus.NotEvaluated)]
    [InlineData("keywords", 513, CheckStatus.Fail)]
    public async Task PrintableAsciiLengthIsNotAByteLimitOrAWholeRulePass(string field, int length, CheckStatus expected)
    {
        var value = new string('A', length);
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, editIndex: xml =>
        {
            var leaf = xml.Descendants("leaf").Single();
            if (field == "title") leaf.Element("title")!.Value = value;
            else leaf.SetAttributeValue("keywords", value);
        });
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal(field == "title" ? "title" : "@keywords", finding.Location.FieldPath);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal("regional", finding.Location.LeafId);
            Assert.Equal("513 printable ASCII characters", finding.Actual);
        }
        else Assert.Empty(check.Findings);
    }
}
