using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaExtraneousContentAttributeTests
{
    [Theory]
    [InlineData("leaf", "manufacturer")]
    [InlineData("leaf", "species")]
    [InlineData("extension", "manufacturer")]
    [InlineData("extension", "keywords")]
    [InlineData("nested-leaf", "manufacturer")]
    [InlineData("nested-extension", "species")]
    public async Task ExplicitUndeclaredContentAttributesAreLocated(string kind, string attribute)
    {
        var result = await Inspect(kind, attribute, "Acme");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368");
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("@" + attribute, finding.Location.FieldPath);
        Assert.Equal("Acme", finding.Actual);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.Contains(kind.EndsWith("leaf", StringComparison.Ordinal) ? "leaf[" : "node-extension[", finding.Location.NodePath);
        Assert.True(finding.Location.Line > 0);
        Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
    }

    [Theory]
    [InlineData("leaf", "application-version", "PDF 1.4", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "version", "1", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "font-library", "fonts", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "keywords", "text", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "xml:lang", "en", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "link:role", "role", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "link:show", "new", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "link:actuate", "onRequest", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "xmlns:extra", "urn:other", "valid", CheckStatus.Pass)]
    [InlineData("extension", "ID", "extension-new", "valid", CheckStatus.Pass)]
    [InlineData("extension", "xml:lang", "en", "valid", CheckStatus.Pass)]
    [InlineData("extension", "xmlns:extra", "urn:other", "valid", CheckStatus.Pass)]
    [InlineData("leaf", "extra:keywords", "text", "valid", CheckStatus.Fail)]
    [InlineData("leaf", "href", "file.pdf", "valid", CheckStatus.Fail)]
    [InlineData("extension", "link:href", "file.pdf", "valid", CheckStatus.Fail)]
    [InlineData("leaf", "manufacturer", "", "valid", CheckStatus.Fail)]
    [InlineData("leaf", "manufacturer", "Acme", "wrong-parent", CheckStatus.NotEvaluated)]
    [InlineData("extension", "manufacturer", "Acme", "forbidden-extension", CheckStatus.NotEvaluated)]
    [InlineData("leaf", "manufacturer", "Acme", "namespace", CheckStatus.Pass)]
    [InlineData("leaf", "manufacturer", "Acme", "wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("leaf", "manufacturer", "Acme", "regional", CheckStatus.Pass)]
    public async Task AllowedAttributesAndUnqualifiedPlacementsRemainDistinct(string kind, string attribute, string value, string variant, CheckStatus expected)
    {
        var result = await Inspect(kind, attribute, value, variant);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
        Assert.Equal(expected == CheckStatus.Pass ? CheckStatus.NotEvaluated : expected,
            Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1368").CheckStatus);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-26", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-26", CheckStatus.Fail)]
    public async Task SourceDateAndSharedAllowanceRemainRequired(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("leaf", "manufacturer", "Acme", variant, date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    private static Task<PackageDeliveryInspection> Inspect(string kind, string attribute, string value, string variant = "valid", string? date = "2026-11-26")
    {
        return FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant == "namespace" ? "valid" : variant, date,
            editIndex: variant == "regional" ? null : Edit, editRegional: variant == "regional" ? Edit : null);
        void Edit(XDocument xml)
        {
            var leaf = new XElement("leaf", new XAttribute("ID", "content"), new XAttribute("operation", "new"),
                new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m2/content.txt"),
                new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XElement("title", "Content"));
            var extension = new XElement("node-extension", new XAttribute("ID", "extension"), new XElement("title", "Extension"), leaf);
            var target = kind.EndsWith("leaf", StringComparison.Ordinal) ? leaf : extension;
            if (attribute.StartsWith("link:", StringComparison.Ordinal))
            {
                target.SetAttributeValue(XNamespace.Xmlns + "link", "http://www.w3c.org/1999/xlink");
                target.SetAttributeValue(XName.Get(attribute[5..], "http://www.w3c.org/1999/xlink"), value);
            }
            else if (attribute == "xml:lang") target.SetAttributeValue(XNamespace.Xml + "lang", value);
            else if (attribute == "xmlns:extra") target.SetAttributeValue(XNamespace.Xmlns + "extra", value);
            else if (attribute == "extra:keywords")
            {
                target.SetAttributeValue(XNamespace.Xmlns + "extra", "urn:other");
                target.SetAttributeValue(XName.Get("keywords", "urn:other"), value);
            }
            else target.SetAttributeValue(attribute, value);
            if (variant == "namespace") target.Name = XName.Get(target.Name.LocalName, "urn:other");
            XElement content = kind == "leaf" ? leaf : extension;
            if (kind == "nested-extension") content = new XElement("node-extension", new XAttribute("ID", "outer"), new XElement("title", "Outer"), extension);
            if (variant == "wrong-parent") xml.Root!.Add(content);
            else if (variant == "forbidden-extension") xml.Root!.Add(new XElement("m2-common-technical-document-summaries", content));
            else xml.Root!.Add(new XElement("m2-common-technical-document-summaries", new XElement("m2-5-clinical-overview", content)));
            if (variant == "wrong-root") xml.Root!.Name = XName.Get("other", "http://www.ich.org/ectd");
        }
    }
}
