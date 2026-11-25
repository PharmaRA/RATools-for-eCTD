using System.Globalization;
using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfNonRelativeBookmarkRuleTests
{
    private const string RuleId = "FDA47-5115";

    [Theory]
    [InlineData("/submission/target.pdf", "bookmark", CheckStatus.Fail)]
    [InlineData("/C/submission/target.pdf", "bookmark", CheckStatus.Fail)]
    [InlineData("//server/share/target.pdf", "bookmark", CheckStatus.Fail)]
    [InlineData("/submission/target.pdf", "filespec", CheckStatus.Fail)]
    [InlineData("/submission/target.pdf", "same-uf", CheckStatus.Fail)]
    [InlineData("target.pdf", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("../0000/target.pdf", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("%2Fsubmission/target.pdf", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("/submission/%2Ftarget.pdf", "bookmark", CheckStatus.Fail)]
    [InlineData("/", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("/submission/", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("C:/submission/target.pdf", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/target.pdf", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "page-link", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "open-action", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "uri", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "conflict-uf", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "filesystem", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "embedded", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "conflict-dest", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "bad-parent", CheckStatus.NotEvaluated)]
    [InlineData("/submission/target.pdf", "next-action", CheckStatus.NotEvaluated)]
    public async Task LiteralAbsoluteFileSpecificationRequiresQualifiedBookmark(string file, string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(file, scenario)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Equal("AbsoluteFileSpecification", finding.Actual);
            Assert.Contains("future submissions", finding.RecommendedAction);
            Assert.DoesNotContain("Replace", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("/target.pdf"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("no-bookmarks")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("label-only")]
    [InlineData("historical-source")]
    public async Task UnboundOrContradictoryFactsDoNotProveNonRelativeBookmark(string scenario)
    {
        var fixture = await Fixture(Build("/target.pdf"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], scenario != "no-bookmarks", [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("outline[1]/GoToR", null, new(FileSpecification: "/target.pdf"))], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            bookmarkFiles: scenario == "label-only" ? [] : [new("outline[1]/A/F", "/target.pdf")]);
        var run = new FdaPdfNonRelativeBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(scenario == "historical-source" ? "0000/report.pdf" : file.LogicalPath, facts,
            scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("/target.pdf")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfNonRelativeBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("/target.pdf")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfNonRelativeBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(4, CheckStatus.NotEvaluated)]
    [InlineData(5, CheckStatus.Fail)]
    public async Task SharedBudgetOnlyRetainsAllocatedFindings(int limit, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("/target.pdf"), limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Theory]
    [InlineData(false, CheckStatus.NotApplicable)]
    [InlineData(true, CheckStatus.NotEvaluated)]
    public async Task NoPdfAndUnreadableSourceAreDifferent(bool broken, CheckStatus expected)
    {
        var result = await Run(await Fixture(broken ? "broken"u8.ToArray() : null));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task MissingArchivedSourceRemainsUnknown()
    {
        var result = await Run(await Fixture(Build("/target.pdf"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialReportRetainsClassificationWithoutOriginalTargetPath()
    {
        const string target = "/synthetic-local-directory/private-file.pdf";
        var fixture = await Fixture(Build(target));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await Run(fixture);
        var finding = Assert.Single(Assert.Single(result.Checks, item => item.RuleId == RuleId).Findings);
        Assert.DoesNotContain(target, System.Text.Json.JsonSerializer.Serialize(finding));
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, string? date = "2026-11-26", PackageReadLimits? limits = null, bool missingSource = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, item => item.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(pdf, limits ?? new());
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(byte[]? pdf, PackageReadLimits limits)
        {
            Limits = limits; var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (pdf is not null) _files["0001/report.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5115";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string file, string scenario = "bookmark")
    {
        var specification = scenario switch {
            "filespec" => "<< /Type /Filespec /F (" + file + ") >>",
            "same-uf" => "<< /F (" + file + ") /UF (" + file + ") >>",
            "conflict-uf" => "<< /F (" + file + ") /UF (relative.pdf) >>",
            "filesystem" => "<< /FS /URL /F (" + file + ") >>",
            "embedded" => "<< /F (" + file + ") /EF << /F 9 0 R >> >>", _ => "(" + file + ")" };
        var action = scenario == "uri" ? "<< /S /URI /URI (" + file + ") >>" :
            "<< /S /GoToR /F " + specification + " /D [0 /Fit]" + (scenario == "next-action" ? " /Next << /S /GoToR /F (other.pdf) /D [0 /Fit] >>" : "") + " >>";
        if (scenario == "page-link") return PdfPigPackageInspectorTests.Build("/A " + action);
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
            "<< /Title (bookmark) /Parent " + (scenario == "bad-parent" ? "1" : "7") + " 0 R /A " + action + (scenario == "conflict-dest" ? " /Dest [3 0 R /Fit]" : "") + " >>");
    }
}
