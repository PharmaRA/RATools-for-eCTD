using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Validation.Rules;

public sealed class FileNamingConventionRuleTests
{
    [Fact]
    public void Evaluate_ReportsUppercaseOrSpaceInFileName()
    {
        var rule = new FileNamingConventionRule();
        var package = CreatePackage(CreateLeaf(fileName: "Study Report.PDF", href: "m5/study-report.pdf"));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("FDA-NAMING-1", finding.RuleId);
        Assert.Equal("FileNaming", finding.Category);
        Assert.Equal(EctdValidationSeverity.Medium, finding.Severity);
        Assert.Contains("Study Report.PDF", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Rename the file", finding.RecommendedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReportsPublishedHrefLongerThanLimit()
    {
        var rule = new FileNamingConventionRule();
        var longHref = $"m5/{new string('a', 231)}.pdf";
        var package = CreatePackage(CreateLeaf(fileName: "study-report.pdf", href: longHref));

        var finding = Assert.Single(rule.Evaluate(CreateContext(package)));

        Assert.Equal("FDA-NAMING-1", finding.RuleId);
        Assert.Equal("FileNaming", finding.Category);
        Assert.Equal(EctdValidationSeverity.Medium, finding.Severity);
        Assert.Contains("exceeds the 230-character", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Shorten folder or file names", finding.RecommendedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReturnsNoFindingsForLowercaseHyphenatedPdf()
    {
        var rule = new FileNamingConventionRule();
        var package = CreatePackage(CreateLeaf(fileName: "study-report.pdf", href: "m5/study-report.pdf"));

        var findings = rule.Evaluate(CreateContext(package)).ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_ReturnsNoFindingsWhenPackageIsMissing()
    {
        var rule = new FileNamingConventionRule();

        var findings = rule.Evaluate(CreateContext(null)).ToArray();

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

    private static EctdSequencePackage CreatePackage(EctdLeaf leaf)
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
                    leaf.Md5)
            ]);

    private static EctdLeaf CreateLeaf(string fileName, string href)
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
            Path.Combine(Path.GetTempPath(), fileName),
            1234,
            new string('a', 64),
            new string('b', 32),
            null);
    }
}
