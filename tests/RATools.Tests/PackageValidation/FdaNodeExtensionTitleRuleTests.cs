using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaNodeExtensionTitleRuleTests
{
    [Theory]
    [InlineData(false, "missing", CheckStatus.Fail)]
    [InlineData(true, "missing", CheckStatus.Fail)]
    [InlineData(false, "title", CheckStatus.Pass)]
    [InlineData(true, "title", CheckStatus.Pass)]
    [InlineData(false, "empty", CheckStatus.Pass)]
    [InlineData(true, "empty", CheckStatus.Pass)]
    [InlineData(false, "blank", CheckStatus.Pass)]
    [InlineData(true, "blank", CheckStatus.Pass)]
    [InlineData(false, "foreign", CheckStatus.Fail)]
    [InlineData(true, "foreign", CheckStatus.Fail)]
    [InlineData(false, "nested-only", CheckStatus.Fail)]
    [InlineData(true, "nested-only", CheckStatus.Fail)]
    [InlineData(false, "late-title", CheckStatus.Pass)]
    [InlineData(true, "late-title", CheckStatus.Pass)]
    [InlineData(false, "duplicate-title", CheckStatus.Pass)]
    [InlineData(true, "duplicate-title", CheckStatus.Pass)]
    public async Task RequiresADirectTitleElementWithoutInventingAContentRequirement(bool regional, string scenario, CheckStatus expected)
    {
        var result = await Inspect(regional, scenario);
        var document = Assert.Single(Target(result).Backbones, doc => doc.Kind == (regional ? BackboneKind.UsRegional : BackboneKind.Ich));
        Assert.True(document.ReadComplete);
        var extension = Assert.Single(document.Elements, element => element.Name == "node-extension" && element.Attribute("ID") == "outer-extension");
        var directTitles = document.Elements.Where(element => element.ParentPath == extension.NodePath && element.Name == "title" && element.NamespaceUri.Length == 0).ToArray();
        Assert.Equal(expected == CheckStatus.Fail ? 0 : scenario == "duplicate-title" ? 2 : 1, directTitles.Length);
        if (scenario is "title" or "empty" or "blank") Assert.Equal(CheckStatus.Pass, document.DtdStatus);
        else Assert.Equal(CheckStatus.Fail, document.DtdStatus);
        if (scenario == "empty") Assert.Equal("", Assert.Single(directTitles).Text);
        if (scenario == "blank") Assert.Equal(" ", Assert.Single(directTitles).Text);
        AssertCheck(result, expected, expected == CheckStatus.Fail ? extension.NodePath : null);
    }

    [Theory]
    [InlineData("missing-tail-broken")]
    [InlineData("open-extension")]
    [InlineData("open-after-title")]
    public async Task IncompleteDocumentCannotProveTheFinalAbsenceOfTitle(string scenario)
    {
        var result = await Inspect(false, scenario);
        var document = Assert.Single(Target(result).Backbones, doc => doc.Kind == BackboneKind.Ich);
        Assert.False(document.ReadComplete);
        Assert.Contains(document.Elements, element => element.Name == "node-extension");
        AssertCheck(result, CheckStatus.NotEvaluated);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.Fail)]
    public async Task KnownFailureAndMissingEvidenceRemainSeparate(string variant, string? date, CheckStatus expected)
        => AssertCheck(await Inspect(false, "missing", variant, date), expected);

    [Fact]
    public async Task NoTargetExtensionsPassWhenTheScopeWasRead()
        => AssertCheck(await Inspect(false, "none"), CheckStatus.Pass);

    [Fact]
    public async Task DefaultForeignNamespaceExtensionIsNotAnEctdExtension()
    {
        var result = await Inspect(false, "foreign-extension");
        var element = Assert.Single(Target(result).Backbones.SelectMany(doc => doc.Elements), item => item.Name == "node-extension");
        Assert.Equal("urn:other", element.NamespaceUri);
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task UnreadRegionalPreventsACompletePass()
        => AssertCheck(await Inspect(false, "title", "unread"), CheckStatus.NotEvaluated);

    [Fact]
    public async Task HistoryWithoutTitleIsNotATargetFailure()
    {
        var result = await Inspect(false, "history", "history-1478");
        var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.LogicalPath == "0000/index.xml");
        Assert.True(previous.ReadComplete);
        var extension = Assert.Single(previous.Elements, element => element.Name == "node-extension");
        Assert.DoesNotContain(previous.Elements, element => element.ParentPath == extension.NodePath && element.Name == "title");
        AssertCheck(result, CheckStatus.Pass);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect(false, "missing", "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1478");

    private static PackageXmlInspection Target(PackageDeliveryInspection result)
        => Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest);

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected, string? nodePath = null)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1478");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1478").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        if (nodePath is not null) Assert.Equal(nodePath, finding.Location.NodePath);
        Assert.Equal("title", finding.Location.FieldPath);
        Assert.Equal(finding.Location.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
    }

    private static Task<PackageDeliveryInspection> Inspect(bool regional, string scenario, string variant = "valid", string? date = "2026-11-20")
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single();
                leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5");
                if (!regional && scenario != "none") AddIndex(xml, scenario == "history" ? "title" : scenario);
            },
            editRegional: regional ? xml =>
            {
                var module = xml.Root!.Element("m1-regional")!;
                var cover = new XElement("m1-2-cover-letters", Extension(scenario));
                if (module.Element("m1-1-forms") is { } forms) forms.AddAfterSelf(cover); else module.AddFirst(cover);
            } : null,
            editIndexText: xml =>
            {
                if (scenario == "missing-tail-broken") return xml + "<broken";
                if (scenario is "open-extension" or "open-after-title")
                    return xml[..xml.IndexOf("<node-extension", StringComparison.Ordinal)] + "<node-extension ID='outer-extension'>" +
                        (scenario == "open-after-title" ? "<title>A</title>" : "");
                return xml;
            },
            editSource: scenario == "history" ? source =>
            {
                var path = Path.Combine(source, "0000/index.xml"); var xml = XDocument.Load(path); AddIndex(xml, "missing"); xml.Save(path);
            } : null);

    private static void AddIndex(XDocument xml, string scenario)
        => xml.Root!.Add(new XElement("m2-common-technical-document-summaries", new XElement("m2-2-introduction", Extension(scenario))));

    private static XElement Extension(string scenario)
    {
        var leaf = new XElement("leaf", new XAttribute("ID", "extension-leaf"), new XAttribute("operation", "new"),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m2/content.txt"), new XElement("title", "Leaf title is not extension title"));
        var extension = new XElement("node-extension", new XAttribute("ID", "outer-extension"), leaf);
        if (scenario == "foreign-extension")
        {
            extension.Name = XName.Get("node-extension", "urn:other");
            extension.SetAttributeValue("xmlns", "urn:other");
        }
        if (scenario == "nested-only") leaf.ReplaceWith(new XElement("node-extension", new XAttribute("ID", "inner-extension"), new XElement("title", "Inner title"), leaf));
        if (scenario is "title" or "empty" or "blank" or "foreign" or "duplicate-title")
            extension.AddFirst(new XElement(scenario == "foreign" ? XName.Get("title", "urn:other") : "title", scenario == "empty" ? "" : scenario == "blank" ? " " : "Extension"));
        if (scenario == "duplicate-title") extension.AddFirst(new XElement("title", "Another"));
        if (scenario == "late-title") extension.Add(new XElement("title", "Late"));
        return extension;
    }
}
