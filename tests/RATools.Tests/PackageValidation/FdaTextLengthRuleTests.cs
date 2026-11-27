using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaTextLengthRuleTests
{
    [Theory]
    [InlineData("\u4dff", CheckStatus.NotEvaluated)]
    [InlineData("\u4e00", CheckStatus.Fail)]
    [InlineData("\u9fff", CheckStatus.Fail)]
    [InlineData("\ua000", CheckStatus.NotEvaluated)]
    [InlineData("\u0301", CheckStatus.NotEvaluated)]
    [InlineData("\U00020000", CheckStatus.NotEvaluated)]
    [InlineData("\U0001f600", CheckStatus.NotEvaluated)]
    [InlineData("\u200d", CheckStatus.NotEvaluated)]
    [InlineData("\ufe0f", CheckStatus.NotEvaluated)]
    [InlineData("\U000e0100", CheckStatus.NotEvaluated)]
    [InlineData("\t", CheckStatus.NotEvaluated)]
    public async Task UnsupportedCharactersDoNotBorrowTheBasicCjkCount(string suffix, CheckStatus expected)
    {
        var value = new string('文', 512) + suffix;
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, editIndex: xml =>
        {
            var leaf = xml.Descendants("leaf").Single();
            leaf.Element("title")!.Value = value;
            leaf.SetAttributeValue("keywords", value);
        });
        var index = Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).Backbones, item => item.LogicalPath == "0001/index.xml");
        Assert.True(index.ReadComplete);
        Assert.Equal(value, Assert.Single(index.Elements, item => item.LocalName == "title").Text);
        Assert.Equal(value, Assert.Single(index.Elements, item => item.LocalName == "leaf").Attributes["keywords"].ExplicitValue);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 2 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(512, false, CheckStatus.NotEvaluated)]
    [InlineData(513, false, CheckStatus.Fail)]
    [InlineData(512, true, CheckStatus.NotEvaluated)]
    [InlineData(513, true, CheckStatus.Fail)]
    public async Task BasicCjkRetainedTextIsIndependentOfTruncationAndXmlEncoding(int length, bool entities, CheckStatus expected)
    {
        var value = new string('文', length);
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editIndex: xml => xml.Descendants("leaf").Single().Element("title")!.Value = value,
            editIndexText: xml => entities ? xml.Replace(value, string.Concat(Enumerable.Repeat("&#x6587;", length)), StringComparison.Ordinal) :
                xml[..(xml.IndexOf(value, StringComparison.Ordinal) + value.Length)] + "<");
        var index = Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).Backbones, item => item.LogicalPath == "0001/index.xml");
        Assert.Equal(entities, index.ReadComplete);
        Assert.Equal(value, Assert.Single(index.Elements, item => item.LocalName == "title").Text);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("title", 512, CheckStatus.NotEvaluated)]
    [InlineData("title", 513, CheckStatus.Fail)]
    [InlineData("keywords", 512, CheckStatus.NotEvaluated)]
    [InlineData("keywords", 513, CheckStatus.Fail)]
    public async Task RegionalBasicCjkValuesRetainTheirOwnLocation(string field, int length, CheckStatus expected)
    {
        var value = new string('文', length);
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            editRegional: xml => xml.Descendants("form").First().Add(new XElement("leaf", new XAttribute("ID", "regional-content"),
                new XAttribute("operation", "new"), field == "keywords" ? new XAttribute("keywords", value) : null,
                new XElement("title", field == "title" ? value : "Short"))));
        var regional = Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).Backbones, item => item.LogicalPath == "0001/m1/us/us-regional.xml");
        Assert.Equal(value, field == "title" ? Assert.Single(regional.Elements, item => item.LocalName == "title").Text :
            Assert.Single(regional.Elements, item => item.LocalName == "leaf").Attributes["keywords"].ExplicitValue);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
            Assert.Equal("regional-content", finding.Location.LeafId);
            Assert.Equal(field == "title" ? "title" : "@keywords", finding.Location.FieldPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("title", 512, false, CheckStatus.NotEvaluated)]
    [InlineData("title", 513, false, CheckStatus.Fail)]
    [InlineData("keywords", 512, false, CheckStatus.NotEvaluated)]
    [InlineData("keywords", 513, false, CheckStatus.Fail)]
    [InlineData("title", 512, true, CheckStatus.NotEvaluated)]
    [InlineData("title", 513, true, CheckStatus.Fail)]
    [InlineData("keywords", 512, true, CheckStatus.NotEvaluated)]
    [InlineData("keywords", 513, true, CheckStatus.Fail)]
    public async Task BasicCjkAndAsciiUseTheCharacterBoundaryNotUtf8Bytes(string field, int length, bool mixed, CheckStatus expected)
    {
        var value = mixed ? new string('文', length - 1) + "A" : new string('文', length);
        Assert.Equal(length, value.EnumerateRunes().Count());
        Assert.Equal(length, new System.Globalization.StringInfo(value).LengthInTextElements);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(value) > 1024);
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, editIndex: xml =>
        {
            var leaf = xml.Descendants("leaf").Single();
            if (field == "title") leaf.Element("title")!.Value = value;
            else leaf.SetAttributeValue("keywords", value);
        });
        var index = Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).Backbones, item => item.LogicalPath == "0001/index.xml");
        Assert.True(index.ReadComplete);
        Assert.Equal(value, field == "title" ? Assert.Single(index.Elements, item => item.LocalName == "title").Text :
            Assert.Single(index.Elements, item => item.LocalName == "leaf").Attributes["keywords"].ExplicitValue);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1500");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(field == "title" ? "title" : "@keywords", finding.Location.FieldPath);
            Assert.Equal("513 characters (printable ASCII or basic CJK)", finding.Actual);
        }
        else Assert.Empty(check.Findings);
        Assert.NotEqual(CheckStatus.Pass, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1500").CheckStatus);
    }

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
    [InlineData("basic-cjk", CheckStatus.Fail)]
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
            "basic-cjk" => new string('文', 513), "combining" => string.Concat(Enumerable.Repeat("e\u0301", 300)),
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
            Assert.Equal("513 characters (printable ASCII or basic CJK)", finding.Actual);
        }
        else Assert.Empty(check.Findings);
    }
}
