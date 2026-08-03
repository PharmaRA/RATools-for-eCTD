using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;
using RATools.Application.Validation.Rules.Pdf;

namespace RATools.Tests.Validation.Rules.Pdf;

public sealed class PdfComplianceRuleTests
{
    [Fact]
    public void Evaluate_ReportsEncryptedPdf()
    {
        using var fixture = TempPdfFixture.Create("encrypted.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: true,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [])));
        var package = CreatePackage(CreateLeaf("encrypted.pdf", "m5/encrypted.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_ENCRYPTED", finding.RuleId);
        Assert.Equal("PdfCompliance", finding.Category);
        Assert.Equal(EctdValidationSeverity.High, finding.Severity);
        Assert.Contains("encrypted.pdf", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReportsNoSearchableText()
    {
        using var fixture = TempPdfFixture.Create("scan.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: false,
            HasSearchableText: false,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [])));
        var package = CreatePackage(CreateLeaf("scan.pdf", "m5/scan.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_NO_SEARCHABLE_TEXT", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.High, finding.Severity);
    }

    [Fact]
    public void Evaluate_ReportsNonEmbeddedFonts()
    {
        using var fixture = TempPdfFixture.Create("font.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: false,
            ["Helvetica"],
            HasBookmarks: true,
            [])));
        var package = CreatePackage(CreateLeaf("font.pdf", "m5/font.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_FONT_NOT_EMBEDDED", finding.RuleId);
        Assert.Contains("Helvetica", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReportsMissingInterDocumentLinkTarget()
    {
        using var fixture = TempPdfFixture.Create("source.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [new PdfLinkReference(PdfLinkKind.InterDocument, "../missing.pdf", 1)])));
        var package = CreatePackage(CreateLeaf("source.pdf", "m5/source.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_BROKEN_INTER_LINK", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.High, finding.Severity);
        Assert.Contains("../missing.pdf", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReportsMissingIntraDocumentLinkTarget()
    {
        using var fixture = TempPdfFixture.Create("source.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [new PdfLinkReference(PdfLinkKind.IntraDocument, "#page=9", 1)],
            PageCount: 3)));
        var package = CreatePackage(CreateLeaf("source.pdf", "m5/source.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_BROKEN_INTRA_LINK", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.Medium, finding.Severity);
        Assert.Contains("#page=9", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReportsUnverifiedFontEmbeddingAsLowSeverity()
    {
        // 无法判定（null）≠ 未嵌入（false）：发 Low 级提示供人工核实，而非 High 级阻断。
        using var fixture = TempPdfFixture.Create("unknown-fonts.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: null,
            [],
            HasBookmarks: true,
            [])));
        var package = CreatePackage(CreateLeaf("unknown-fonts.pdf", "m5/unknown-fonts.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_FONT_EMBEDDING_UNVERIFIED", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.Low, finding.Severity);
    }

    [Fact]
    public void Evaluate_DoesNotReportSecurityRestrictionWhenStateIsUnknown()
    {
        using var fixture = TempPdfFixture.Create("unknown-security.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: null,
            HasSearchableText: true,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [])));
        var package = CreatePackage(CreateLeaf("unknown-security.pdf", "m5/unknown-security.pdf", fixture.Path));

        var findings = rule.Evaluate(CreateContext(package)).ToArray();

        Assert.DoesNotContain(findings, x => x.RuleId == "PDF_SECURITY_RESTRICTED");
    }

    [Fact]
    public void Evaluate_ReturnsNoFindingsForCompliantPdf()
    {
        using var fixture = TempPdfFixture.Create("clean.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(new PdfInspectionResult(
            "1.7",
            IsEncrypted: false,
            HasSecurityRestrictions: false,
            HasSearchableText: true,
            AllFontsEmbedded: true,
            [],
            HasBookmarks: true,
            [new PdfLinkReference(PdfLinkKind.InterDocument, "target.pdf", 1)])));
        var package = CreatePackage(
            CreateLeaf("clean.pdf", "m5/clean.pdf", fixture.Path),
            new EctdPublishedFile(Guid.NewGuid(), fixture.Path, "m5/target.pdf", "target.pdf", 1, "sha", "md5"));

        var findings = rule.Evaluate(CreateContext(package)).ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_ReportsLegacyPdfVersionAsLowSeverity()
    {
        using var fixture = TempPdfFixture.Create("legacy.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { PdfVersion = "1.3" }));
        var package = CreatePackage(CreateLeaf("legacy.pdf", "m5/legacy.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_VERSION_LEGACY", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.Low, finding.Severity);
    }

    [Theory]
    [InlineData("1.4")]
    [InlineData("1.7")]
    public void Evaluate_AcceptsCustomaryPdfVersionRange(string version)
    {
        using var fixture = TempPdfFixture.Create("ok.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { PdfVersion = version }));
        var package = CreatePackage(CreateLeaf("ok.pdf", "m5/ok.pdf", fixture.Path));

        var findings = rule.Evaluate(CreateContext(package)).ToArray();

        Assert.DoesNotContain(findings, x => x.RuleId == "PDF_VERSION_LEGACY");
        Assert.DoesNotContain(findings, x => x.RuleId == "PDF_VERSION_UNSUPPORTED");
    }

    [Fact]
    public void Evaluate_ReportsDeeplyNestedBookmarks()
    {
        using var fixture = TempPdfFixture.Create("deep.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { BookmarkMaxDepth = 5 }));
        var package = CreatePackage(CreateLeaf("deep.pdf", "m5/deep.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_BOOKMARK_TOO_DEEP", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.Low, finding.Severity);
        Assert.Contains("5", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_AcceptsBookmarkDepthAtRecommendedLimit()
    {
        using var fixture = TempPdfFixture.Create("depth4.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { BookmarkMaxDepth = 4 }));
        var package = CreatePackage(CreateLeaf("depth4.pdf", "m5/depth4.pdf", fixture.Path));

        Assert.DoesNotContain(
            rule.Evaluate(CreateContext(package)),
            x => x.RuleId == "PDF_BOOKMARK_TOO_DEEP");
    }

    [Fact]
    public void Evaluate_DoesNotReportBookmarkDepthWhenUnknown()
    {
        using var fixture = TempPdfFixture.Create("unknown-depth.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { BookmarkMaxDepth = null }));
        var package = CreatePackage(CreateLeaf("unknown-depth.pdf", "m5/unknown-depth.pdf", fixture.Path));

        Assert.DoesNotContain(
            rule.Evaluate(CreateContext(package)),
            x => x.RuleId == "PDF_BOOKMARK_TOO_DEEP");
    }

    [Fact]
    public void Evaluate_ReportsInitialViewNotShowingBookmarks()
    {
        using var fixture = TempPdfFixture.Create("view.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { PageMode = "UseNone" }));
        var package = CreatePackage(CreateLeaf("view.pdf", "m5/view.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_INITIAL_VIEW_NOT_OUTLINES", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.Low, finding.Severity);
    }

    [Fact]
    public void Evaluate_AcceptsUseOutlinesInitialView()
    {
        using var fixture = TempPdfFixture.Create("outlines.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with { PageMode = "UseOutlines" }));
        var package = CreatePackage(CreateLeaf("outlines.pdf", "m5/outlines.pdf", fixture.Path));

        Assert.DoesNotContain(
            rule.Evaluate(CreateContext(package)),
            x => x.RuleId == "PDF_INITIAL_VIEW_NOT_OUTLINES");
    }

    [Fact]
    public void Evaluate_DoesNotReportInitialViewWhenDocumentHasNoBookmarks()
    {
        // 无书签的文档谈不上"该以书签面板打开"；此处只应出现 PDF_NO_BOOKMARKS。
        using var fixture = TempPdfFixture.Create("no-bookmarks.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with
        {
            HasBookmarks = false,
            BookmarkMaxDepth = 0,
            PageMode = "UseNone",
            PageCount = 12
        }));
        var package = CreatePackage(CreateLeaf("no-bookmarks.pdf", "m5/no-bookmarks.pdf", fixture.Path));

        var findings = rule.Evaluate(CreateContext(package)).ToArray();

        Assert.DoesNotContain(findings, x => x.RuleId == "PDF_INITIAL_VIEW_NOT_OUTLINES");
        Assert.Contains(findings, x => x.RuleId == "PDF_NO_BOOKMARKS");
    }

    [Fact]
    public void Evaluate_DoesNotRequireBookmarksForShortDocument()
    {
        // 行为收紧：4 页以下无书签不再打扰（1 页封面信不该被烦扰）。
        using var fixture = TempPdfFixture.Create("short.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with
        {
            HasBookmarks = false,
            BookmarkMaxDepth = 0,
            PageCount = 4
        }));
        var package = CreatePackage(CreateLeaf("short.pdf", "m5/short.pdf", fixture.Path));

        Assert.DoesNotContain(
            rule.Evaluate(CreateContext(package)),
            x => x.RuleId == "PDF_NO_BOOKMARKS");
    }

    [Fact]
    public void Evaluate_RequiresBookmarksFromFivePagesOnward()
    {
        using var fixture = TempPdfFixture.Create("long.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with
        {
            HasBookmarks = false,
            BookmarkMaxDepth = 0,
            PageCount = 5
        }));
        var package = CreatePackage(CreateLeaf("long.pdf", "m5/long.pdf", fixture.Path));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("PDF_NO_BOOKMARKS", finding.RuleId);
        Assert.Equal(EctdValidationSeverity.Medium, finding.Severity);
    }

    [Fact]
    public void Evaluate_RequiresBookmarksWhenPageCountIsUnknown()
    {
        // 页数无法判定时保持"无法判定 ≠ 合规"：仍提示缺书签。
        using var fixture = TempPdfFixture.Create("unknown-pages.pdf");
        var rule = new PdfComplianceRule(new FakePdfInspector(CompliantResult with
        {
            HasBookmarks = false,
            BookmarkMaxDepth = 0,
            PageCount = null
        }));
        var package = CreatePackage(CreateLeaf("unknown-pages.pdf", "m5/unknown-pages.pdf", fixture.Path));

        Assert.Contains(
            rule.Evaluate(CreateContext(package)),
            x => x.RuleId == "PDF_NO_BOOKMARKS");
    }

    // 全合规基线：各用例用 `with` 只改被测那一个字段，避免每处重复 11 个构造参数。
    private static readonly PdfInspectionResult CompliantResult = new(
        "1.7",
        IsEncrypted: false,
        HasSecurityRestrictions: false,
        HasSearchableText: true,
        AllFontsEmbedded: true,
        [],
        HasBookmarks: true,
        [],
        PageCount: 10,
        BookmarkMaxDepth: 2,
        PageMode: "UseOutlines");

    private static EctdValidationContext CreateContext(EctdSequencePackage? package)
    {
        var profile = new StandardsProfile(
            "us-fda-ectd-3.2.2",
            "US FDA eCTD 3.2.2",
            "FDA",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            "4.5",
            [],
            []);
        return new EctdValidationContext(profile, new ValidateSequenceRequest(Guid.NewGuid(), "0000"), package, null);
    }

    private static EctdSequencePackage CreatePackage(EctdLeaf leaf, params EctdPublishedFile[] extraPublishedFiles)
        => new(
            Guid.NewGuid(),
            "ANDA123456",
            "0000",
            "US FDA eCTD 3.2.2",
            "3.2.2",
            "3.3",
            BackboneXmlProfiles.FdaEctd322UsRegional33,
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-3.2.2", "anda"),
            new EctdSequenceMetadata("0000", "original-application", "initial", "Initial sequence", "Acme Pharma", "356h"),
            new EctdUsRegionalMetadata(
                "ANDA123456",
                "Acme Pharma",
                "Initial sequence",
                "Jane Doe",
                "regulatory",
                "555-0100",
                "office",
                "jane@example.com",
                "anda",
                "original-application",
                "initial",
                "356h"),
            [],
            [leaf],
            [
                new EctdPublishedFile(
                    leaf.DocumentId,
                    leaf.SourcePath,
                    leaf.Href,
                    leaf.FileName,
                    leaf.FileSize,
                    leaf.Sha256,
                    leaf.Md5),
                .. extraPublishedFiles
            ]);

    private static EctdLeaf CreateLeaf(string fileName, string href, string sourcePath)
    {
        var placementId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        return new EctdLeaf(
            placementId,
            documentId,
            $"leaf-{placementId:N}",
            "0000",
            "m5-3-5-1",
            "m5",
            "new",
            "Study Report",
            href,
            fileName,
            "application/pdf",
            sourcePath,
            1234,
            new string('a', 64),
            new string('b', 32),
            null);
    }

    private sealed class FakePdfInspector(PdfInspectionResult result) : IPdfInspector
    {
        public PdfInspectionResult Inspect(Stream pdfStream, string relativeHref)
            => result;
    }

    private sealed class TempPdfFixture : IDisposable
    {
        private TempPdfFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempPdfFixture Create(string fileName)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pdf-rule-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, "pdf");
            return new TempPdfFixture(path);
        }

        public void Dispose()
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
