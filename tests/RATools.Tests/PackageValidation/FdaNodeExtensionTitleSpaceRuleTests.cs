using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaNodeExtensionTitleSpaceRuleTests
{
    [Theory]
    [InlineData(false, " A", " A", CheckStatus.Fail)]
    [InlineData(true, " A", " A", CheckStatus.Fail)]
    [InlineData(false, "A ", "A ", CheckStatus.Fail)]
    [InlineData(true, "A ", "A ", CheckStatus.Fail)]
    [InlineData(false, " ", " ", CheckStatus.Fail)]
    [InlineData(true, " ", " ", CheckStatus.Fail)]
    [InlineData(false, "A B", "A B", CheckStatus.Pass)]
    [InlineData(true, "A B", "A B", CheckStatus.Pass)]
    [InlineData(false, "", "", CheckStatus.Pass)]
    [InlineData(true, "", "", CheckStatus.Pass)]
    [InlineData(false, "\tA\t", "\tA\t", CheckStatus.Pass)]
    [InlineData(true, "\tA\t", "\tA\t", CheckStatus.Pass)]
    [InlineData(false, "\nA\n", "\nA\n", CheckStatus.Pass)]
    [InlineData(true, "\nA\n", "\nA\n", CheckStatus.Pass)]
    [InlineData(false, "&#160;A&#160;", "\u00a0A\u00a0", CheckStatus.Pass)]
    [InlineData(true, "&#160;A&#160;", "\u00a0A\u00a0", CheckStatus.Pass)]
    [InlineData(false, "&#x20;A", " A", CheckStatus.Fail)]
    [InlineData(true, "A&#32;", "A ", CheckStatus.Fail)]
    [InlineData(false, "<![CDATA[ A]]>", " A", CheckStatus.Fail)]
    [InlineData(true, "<![CDATA[A ]]>", "A ", CheckStatus.Fail)]
    [InlineData(false, "&amp;#x20;A", "&#x20;A", CheckStatus.Pass)]
    [InlineData(true, "&amp;#x20;A", "&#x20;A", CheckStatus.Pass)]
    public async Task ChecksXmlDecodedBoundarySpacesWithoutTrimmingOrDecodingAgain(bool regional, string raw, string text, CheckStatus expected)
    {
        var result = await Inspect(raw, regional: regional);
        var document = Assert.Single(Target(result).Backbones, doc => doc.Kind == (regional ? BackboneKind.UsRegional : BackboneKind.Ich));
        Assert.True(document.ReadComplete);
        Assert.Equal(CheckStatus.Pass, document.DtdStatus);
        var title = DirectTitle(document);
        Assert.Equal(text, title.Text);
        AssertCheck(result, expected, expected == CheckStatus.Fail ? title : null);
    }

    [Theory]
    [InlineData("open-leading", " A", CheckStatus.Fail)]
    [InlineData("open-trailing", "A ", CheckStatus.NotEvaluated)]
    [InlineData("tail-leading", " A", CheckStatus.Fail)]
    [InlineData("tail-trailing", "A ", CheckStatus.NotEvaluated)]
    public async Task IncompleteTextCannotProveTheFinalTrailingCharacter(string scenario, string text, CheckStatus expected)
    {
        var result = await Inspect(text, scenario);
        var document = Assert.Single(Target(result).Backbones, doc => doc.Kind == BackboneKind.Ich);
        Assert.False(document.ReadComplete);
        var title = DirectTitle(document);
        Assert.Equal(text, title.Text);
        AssertCheck(result, expected, expected == CheckStatus.Fail ? title : null);
    }

    [Theory]
    [InlineData("missing", CheckStatus.Pass)]
    [InlineData("leaf-only-space", CheckStatus.Pass)]
    [InlineData("foreign-title", CheckStatus.Pass)]
    [InlineData("foreign-extension", CheckStatus.Pass)]
    [InlineData("child-content", CheckStatus.NotEvaluated)]
    public async Task OnlyExactSimpleDirectTitlesSupplyBoundaryText(string scenario, CheckStatus expected)
        => AssertCheck(await Inspect(" A ", scenario), expected);

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.Fail)]
    public async Task DateSourceAndKnownFailureRemainSeparate(string variant, string? date, CheckStatus expected)
        => AssertCheck(await Inspect(" A", variant: variant, date: date), expected);

    [Fact]
    public async Task HistoricalBoundarySpacesAreNotTargetFindings()
    {
        var result = await Inspect("A", "history", variant: "history-1482");
        var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.LogicalPath == "0000/index.xml");
        Assert.True(previous.ReadComplete);
        Assert.Equal(" A ", DirectTitle(previous).Text);
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect(" A", variant: "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1482");

    private static PackageXmlInspection Target(PackageDeliveryInspection result)
        => Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest);

    private static ParsedXmlElement DirectTitle(ParsedBackbone document)
    {
        var extension = Assert.Single(document.Elements, element => element.Name == "node-extension");
        return Assert.Single(document.Elements, element => element.ParentPath == extension.NodePath && element.Name == "title");
    }

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected, ParsedXmlElement? title = null)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1482");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1482").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        Assert.Equal("title", finding.Location.FieldPath);
        if (title is not null) { Assert.Equal(title.NodePath, finding.Location.NodePath); Assert.Equal(title.Text, finding.Actual); }
    }

    private static Task<PackageDeliveryInspection> Inspect(string raw, string scenario = "valid", bool regional = false,
        string variant = "valid", string? date = "2026-11-20")
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5");
                if (!regional) AddIndex(xml, scenario);
            },
            editRegional: regional ? xml =>
            {
                var module = xml.Root!.Element("m1-regional")!;
                var cover = new XElement("m1-2-cover-letters", Extension(scenario));
                if (module.Element("m1-1-forms") is { } forms) forms.AddAfterSelf(cover); else module.AddFirst(cover);
            } : null,
            editSource: source =>
            {
                var path = Path.Combine(source, regional ? "0001/m1/us/us-regional.xml" : "0001/index.xml");
                var xml = File.ReadAllText(path).Replace("TITLE-MARKER", raw, StringComparison.Ordinal);
                if (scenario.StartsWith("open-", StringComparison.Ordinal))
                {
                    var start = xml.IndexOf("<node-extension", StringComparison.Ordinal);
                    var titleStart = xml.IndexOf("<title>", start, StringComparison.Ordinal) + "<title>".Length;
                    xml = xml[..titleStart] + raw + "<!--text committed--><broken";
                }
                if (scenario.StartsWith("tail-", StringComparison.Ordinal)) xml += "<broken";
                File.WriteAllText(path, xml);
                if (scenario == "history")
                {
                    var previousPath = Path.Combine(source, "0000/index.xml"); var previous = XDocument.Load(previousPath); AddIndex(previous, "valid"); previous.Save(previousPath);
                    File.WriteAllText(previousPath, File.ReadAllText(previousPath).Replace("TITLE-MARKER", " A ", StringComparison.Ordinal));
                }
            });

    private static void AddIndex(XDocument xml, string scenario)
        => xml.Root!.Add(new XElement("m2-common-technical-document-summaries", new XElement("m2-2-introduction", Extension(scenario))));

    private static XElement Extension(string scenario)
    {
        var leaf = new XElement("leaf", new XAttribute("ID", "extension-leaf"), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)),
            new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m2/content.txt"), new XElement("title", "Leaf"));
        var title = new XElement(scenario == "foreign-title" ? XName.Get("title", "urn:other") : "title", "TITLE-MARKER");
        if (scenario == "child-content") title.Add(new XElement("b", "child"));
        if (scenario == "leaf-only-space") leaf.Element("title")!.Value = " Leaf ";
        var extension = new XElement("node-extension", new XAttribute("ID", "extension"), scenario is "missing" or "leaf-only-space" ? null : title, leaf);
        if (scenario == "foreign-extension") { extension.Name = XName.Get("node-extension", "urn:other"); extension.SetAttributeValue("xmlns", "urn:other"); }
        return extension;
    }
}
