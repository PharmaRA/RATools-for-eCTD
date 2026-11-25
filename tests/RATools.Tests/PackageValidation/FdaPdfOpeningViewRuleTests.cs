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

public sealed class FdaPdfOpeningViewRuleTests
{
    private const string RuleId = "FDA47-5045";

    [Theory]
    [InlineData("", false, CheckStatus.NotEvaluated)]
    [InlineData("", true, CheckStatus.Fail)]
    [InlineData("/PageMode /UseNone", false, CheckStatus.NotEvaluated)]
    [InlineData("/PageMode /UseNone", true, CheckStatus.Fail)]
    [InlineData("/PageMode /UseOutlines", true, CheckStatus.NotEvaluated)]
    [InlineData("/PageMode /UseOutlines", false, CheckStatus.Fail)]
    [InlineData("/PageMode /UseThumbs", true, CheckStatus.Fail)]
    [InlineData("/PageMode /FullScreen", false, CheckStatus.Fail)]
    [InlineData("/PageMode /UseOC", false, CheckStatus.Fail)]
    [InlineData("/PageMode /UseAttachments", true, CheckStatus.Fail)]
    [InlineData("/PageMode (UseOutlines)", true, CheckStatus.NotEvaluated)]
    [InlineData("/PageMode null", true, CheckStatus.NotEvaluated)]
    [InlineData("/PageMode /Unknown", false, CheckStatus.NotEvaluated)]
    [InlineData("/PageMode 9 0 R", true, CheckStatus.Fail)]
    [InlineData("/PageMode /UseThumbs /OpenAction [3 0 R /Fit]", true, CheckStatus.NotEvaluated)]
    [InlineData("/PageMode /UseThumbs /AA << >>", true, CheckStatus.NotEvaluated)]
    public async Task OriginalModeAndBookmarkEvidenceReachIndependentRule(string catalog, bool bookmarks, CheckStatus expected)
    {
        var fixture = await Fixture(Build(catalog, bookmarks));
        var result = await Run(fixture);
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Contains("future submissions", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("/PageMode /UseOutlines /Outlines null")]
    [InlineData("/PageMode /UseOutlines /Outlines << /Count 1 >>")]
    [InlineData("/PageMode /UseOutlines /Outlines << /Last 8 0 R >>")]
    [InlineData("/PageMode /UseOutlines /Outlines << /First 8 0 R >>")]
    public async Task UnknownOutlineStructureIsNotProvenAbsence(string catalog)
    {
        var result = await Run(await Fixture(Build(catalog, false)));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-20", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-21", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task ArchivedViewPolicyIsDateBound(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("/PageMode /UseThumbs", false), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c")]
    [InlineData("113faa128d34ca5c2b99184da2cf4da2e4359403204289892720c1f73afcae2a")]
    public async Task BothArchivedSourcesAreRequired(string hash)
    {
        var result = await Run(await Fixture(Build("/PageMode /UseThumbs", false), missingHash: hash));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false, CheckStatus.NotApplicable)]
    [InlineData(true, CheckStatus.NotEvaluated)]
    public async Task NoPdfAndUnreadablePdfDiffer(bool broken, CheckStatus expected)
    {
        var result = await Run(await Fixture(broken ? "not a pdf"u8.ToArray() : null));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("", false, "UseNone", false, false)]
    [InlineData("", true, "UseNone", true, false)]
    [InlineData("/PageMode /UseOutlines", true, "UseOutlines", true, false)]
    [InlineData("/PageMode (UseNone)", false, null, false, false)]
    [InlineData("/PageMode null", false, null, false, false)]
    [InlineData("/PageMode /UseNone /Outlines null", false, "UseNone", null, false)]
    [InlineData("/OpenAction [3 0 R /Fit]", false, "UseNone", false, true)]
    public void AdapterSeparatesDefaultModeAndUnknownOutlineFacts(string catalog, bool bookmarks, string? mode, bool? presence, bool actions)
    {
        using var stream = new MemoryStream(Build(catalog, bookmarks));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(new PdfOpeningViewFacts(mode, presence, actions), facts.OpeningView);
    }

    [Theory]
    [InlineData("<< /Type /Outlines >>", false, CheckStatus.Fail)]
    [InlineData("<< /Type /Outlines /Count 0 >>", false, CheckStatus.Fail)]
    [InlineData("<< /Type /Outlines /Count 1 >>", null, CheckStatus.NotEvaluated)]
    [InlineData("<< /Type /Outlines /First 8 0 R >>", null, CheckStatus.NotEvaluated)]
    [InlineData("<< /Type /Outlines /Last 8 0 R >>", null, CheckStatus.NotEvaluated)]
    public async Task EmptyOutlineNeedsConsistentOriginalDictionary(string root, bool? presence, CheckStatus expected)
    {
        var fixture = await Fixture(Build("/PageMode /UseOutlines", true, root));
        var result = await Run(fixture);
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(presence, facts.OpeningView!.HasBookmarks);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("<< /Title (Section) /Parent 6 0 R /Dest [3 0 R /Fit] >>")]
    [InlineData("<< /Title 42 /Parent 7 0 R /Dest [3 0 R /Fit] >>")]
    public async Task UnqualifiedOutlineItemCannotSupplyPresence(string item)
    {
        var result = await Run(await Fixture(Build("/PageMode /UseNone", true, item: item)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Null(Assert.Single(result.Documents).Facts!.OpeningView!.HasBookmarks);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, check => check.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task UnresolvedModeDoesNotReclassifyParsedContentAsCorrupt()
    {
        var result = await Run(await Fixture(Build("/PageMode 99 0 R", true)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Null(facts.OpeningView);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, check => check.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("missing-view")]
    [InlineData("missing-facts")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("unknown-bookmarks")]
    [InlineData("contradictory-bookmarks")]
    [InlineData("contradictory-mode")]
    [InlineData("actions")]
    [InlineData("unknown-mode")]
    public async Task UnqualifiedFrozenFactsCannotAuthorizePanelFailure(string scenario)
    {
        var fixture = await Fixture(Build("/PageMode /UseThumbs", true));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], scenario != "contradictory-bookmarks", [],
            PageCount: 1, PageMode: scenario == "contradictory-mode" ? "UseOutlines" : "UseThumbs", ParseError: scenario == "parse" ? "opaque" : null);
        var view = new PdfOpeningViewFacts(scenario == "unknown-mode" ? "Other" : "UseThumbs", scenario == "unknown-bookmarks" ? null : true, scenario == "actions");
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            openingView: scenario == "missing-view" ? null : view);
        var run = new FdaPdfOpeningViewRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, scenario == "missing-facts" ? null : facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceRetainsFailureOnInterruption(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("/PageMode /UseThumbs", true)); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfOpeningViewRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("/PageMode /UseThumbs", true)); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfOpeningViewRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("/PageMode /UseThumbs", CheckStatus.Fail)]
    [InlineData("/PageMode /UseOutlines", CheckStatus.NotEvaluated)]
    public async Task ReportPreservesPartialBoundary(string mode, CheckStatus expected)
    {
        var fixture = await Fixture(Build(mode, true));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task SharedFindingBudgetIncludesOpeningView(int limit)
    {
        var fixture = await Fixture(Build("/PageMode /UseThumbs", true), limits: new() { MaxXmlFindings = limit });
        var result = await Run(fixture);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, check => check.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task KnownMismatchSurvivesLaterDocumentLimit()
    {
        var fixture = await Fixture(Build("/PageMode /UseThumbs", true), secondPdf: true);
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, check => check.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture(Build("/PageMode /UseThumbs", true)); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new FdaPdfOpeningViewRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token));
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "later-limit-opening-view-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            if (_count++ > 0) throw new PdfInspectionLimitException("Later document limit.");
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

    private static byte[] Build(string catalog, bool bookmarks, string? root = null, string? item = null) => PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]",
        catalog + (bookmarks ? " /Outlines 7 0 R" : ""),
        root ?? "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
        item ?? "<< /Title (Section) /Parent 7 0 R /Dest [3 0 R /Fit] >>", "/UseNone");
    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, string? date = "2026-11-25", string? missingHash = null, PackageReadLimits? limits = null, bool secondPdf = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingHash is not null)
        {
            Assert.Contains(snapshot.Resources, item => item.Sha256 == missingHash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != missingHash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != missingHash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(pdf, limits ?? new(), secondPdf);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(byte[]? pdf, PackageReadLimits limits, bool secondPdf)
        {
            Limits = limits; var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (pdf is not null) _files["0001/report.pdf"] = pdf;
            if (pdf is not null && secondPdf) _files["0001/z-other.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5045";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
