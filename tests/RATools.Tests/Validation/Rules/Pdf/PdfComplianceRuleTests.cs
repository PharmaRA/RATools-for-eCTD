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
