using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackagePdfValidatorTests
{
    [Theory]
    [InlineData("/A << /S /GoToR /F (../0000/target.pdf) /D [0 /Fit] >>", null)]
    [InlineData("/A << /S /GoToR /F (../0000/target.pdf) /D (section) >>", null)]
    [InlineData("/A << /S /URI /URI (../0000/target.pdf#page=1) >>", null)]
    [InlineData("/A << /S /URI /URI (../0000/target.pdf#nameddest=section) >>", null)]
    [InlineData("/A << /S /URI /URI (../0000/target.pdf#section) >>", null)]
    [InlineData("/A << /S /GoToR /F (../0000/target.pdf) /D [1 /Fit] >>", "PDF_LINK_PAGE_OUT_OF_RANGE")]
    [InlineData("/A << /S /URI /URI (../0000/target.pdf#page=0) >>", "PDF_DESTINATION_INVALID")]
    [InlineData("/A << /S /URI /URI (../0000/target.pdf#page=1&nameddest=section) >>", "PDF_DESTINATION_INVALID")]
    [InlineData("/A << /S /GoToR /F (../0000/target.pdf) /D (missing) >>", "PDF_NAMED_DESTINATION_MISSING")]
    [InlineData("/A << /S /GoToR /F (../0000/missing.pdf) /D [0 /Fit] >>", "PDF_LINK_FILE_MISSING")]
    [InlineData("/A << /S /GoToR /F (../0002/target.pdf) /D [0 /Fit] >>", "PDF_LINK_OUTSIDE_APPLICATION")]
    [InlineData("/A << /S /URI /URI (../../outside.pdf#page=1) >>", "PDF_LINK_OUTSIDE_APPLICATION")]
    [InlineData("/A << /S /URI /URI (file:///outside.pdf) >>", "PDF_LINK_OUTSIDE_APPLICATION")]
    [InlineData("/A << /S /URI /URI (https://example.invalid/literature) >>", "PDF_EXTERNAL_LINK")]
    public async Task LinksResolveToTheExactSelectedHistoricalFileAndDestination(string action, string? expectedCode)
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await File.WriteAllBytesAsync(fixture.PathOf("0001/source.pdf"), PdfPigPackageInspectorTests.Build(action));
        await File.WriteAllBytesAsync(fixture.PathOf("0000/target.pdf"), PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Dests << /section [3 0 R /Fit] >>"));
        var result = await InspectAsync(fixture, "0001");
        var link = Assert.Single(result.Links, link => link.SourceLogicalPath == "0001/source.pdf");
        if (expectedCode is null || expectedCode == "PDF_EXTERNAL_LINK") Assert.Equal(CheckStatus.Pass, link.Status);
        else Assert.Equal(CheckStatus.Fail, link.Status);
        if (expectedCode is not null) Assert.Contains(Findings(result), finding => finding.Code == expectedCode && finding.Location.LogicalPath == "0001/source.pdf");
        Assert.Equal(CheckStatus.Pass, result.Checks.Single(check => check.RuleId == "PDF-READABLE").Status);
    }

    [Fact]
    public async Task MissingHistoryNeverBorrowsAnUnselectedPhysicalDirectory()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await File.WriteAllBytesAsync(fixture.PathOf("0001/source.pdf"), PdfPigPackageInspectorTests.Build("/A << /S /GoToR /F (../0000/target.pdf) /D [0 /Fit] >>"));
        var result = await InspectAsync(fixture, "0001", history: []);
        Assert.Contains(Findings(result), finding => finding.Code == "PDF_LINK_HISTORY_MISSING" && finding.CheckStatus == CheckStatus.NotEvaluated);
    }

    [Fact]
    public async Task FileSpecificationIsLiteralWhileUriEscapesDecodeExactlyOnce()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await File.WriteAllBytesAsync(fixture.PathOf("0000/name%20literal.pdf"), PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]"));
        await File.WriteAllBytesAsync(fixture.PathOf("0001/specification.pdf"), PdfPigPackageInspectorTests.Build("/A << /S /GoToR /F (../0000/name%20literal.pdf) /D [0 /Fit] >>"));
        await File.WriteAllBytesAsync(fixture.PathOf("0001/uri.pdf"), PdfPigPackageInspectorTests.Build("/A << /S /URI /URI (../0000/name%2520literal.pdf#page=1) >>"));
        var result = await InspectAsync(fixture, "0001");
        Assert.All(result.Links.Where(link => link.SourceLogicalPath.StartsWith("0001/", StringComparison.Ordinal)), link =>
        {
            Assert.Equal("0000/name%20literal.pdf", link.TargetLogicalPath);
            Assert.Equal(CheckStatus.Pass, link.Status);
        });
    }

    [Fact]
    public async Task DuplicateNamedDestinationsAreNotResolvedByTakingTheFirst()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await File.WriteAllBytesAsync(fixture.PathOf("0000/duplicate.pdf"), PdfPigPackageInspectorTests.Build("/Dest (section)",
            "/Dests << /section [3 0 R /Fit] >> /Names << /Dests << /Names [(section) [3 0 R /Fit]] >> >>"));
        var result = await InspectAsync(fixture, "0000");
        Assert.Contains(Findings(result), finding => finding.Code == "PDF_NAMED_DESTINATION_AMBIGUOUS");
    }

    [Fact]
    public async Task BrokenPdfDoesNotPassSecurityFontsOrDestinationChecks()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await File.WriteAllTextAsync(fixture.PathOf("0000/broken.pdf"), "not a pdf");
        await File.WriteAllBytesAsync(fixture.PathOf("0000/source.pdf"), PdfPigPackageInspectorTests.Build("/A << /S /GoToR /F (broken.pdf) /D [0 /Fit] >>"));
        var result = await InspectAsync(fixture, "0000");
        Assert.Equal(CheckStatus.Fail, result.Checks.Single(check => check.RuleId == "PDF-READABLE").Status);
        Assert.Equal(CheckStatus.NotEvaluated, result.Checks.Single(check => check.RuleId == "PDF-SECURITY").Status);
        Assert.Contains(Findings(result), finding => finding.Code == "PDF_LINK_TARGET_UNREADABLE");
    }

    [Theory]
    [InlineData("unknown-security", "PDF_SECURITY_UNKNOWN", CheckStatus.NotEvaluated)]
    [InlineData("restricted", "PDF_SECURITY_RESTRICTED", CheckStatus.Fail)]
    [InlineData("additional-font", "PDF_ADDITIONAL_FONT_NOT_EMBEDDED", CheckStatus.Fail)]
    [InlineData("version", "PDF_VERSION_REQUIRES_REGIONAL_POLICY", CheckStatus.NotEvaluated)]
    [InlineData("unknown-links", "PDF_LINK_INVENTORY_INCOMPLETE", CheckStatus.NotEvaluated)]
    public async Task UnsupportedAndUnknownPdfFactsRemainExplicit(string scenario, string code, CheckStatus status)
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var result = await InspectAsync(fixture, "0000", inspector: new FixedInspector(scenario));
        Assert.Contains(Findings(result), finding => finding.Code == code && finding.CheckStatus == status);
        Assert.NotEqual(CheckStatus.Pass, result.Checks.Single(check => check.RuleId == "PDF-FONTS").Status);
    }

    private sealed class FixedInspector(string scenario) : IPackagePdfInspector
    {
        public string EngineVersion => "test-fixed-" + scenario;
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken) => new(
            new PdfInspectionResult(scenario == "version" ? "1.7" : "1.4", false, scenario == "unknown-security" ? null : scenario == "restricted", true,
                scenario == "additional-font" ? false : null, scenario == "additional-font" ? ["CustomUnembeddedFont"] : [], false, [], PageCount: 1),
            [], [], scenario != "unknown-links", true, scenario == "unknown-links" ? ["Unknown link action"] : []);
    }

    private static async Task<PackagePdfInspection> InspectAsync(PackageFileInspectorTests.Fixture fixture, string sequence,
        string[]? history = null, IPackagePdfInspector? inspector = null)
    {
        var (input, lifecycle) = await fixture.SelectAsync(sequence, history: history);
        var files = await new PackageFileInspector().InspectAsync(input, lifecycle);
        return await new PackagePdfValidator(inspector ?? new PdfPigPackageInspector()).InspectAsync(input, files);
    }
    private static IEnumerable<ValidationFinding> Findings(PackagePdfInspection result) => result.Checks.SelectMany(check => check.Findings);
}
