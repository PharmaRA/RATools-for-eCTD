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

public sealed class FdaPdfExternalBookmarkRuleTests
{
    private const string RuleId = "FDA47-5105";

    [Theory]
    [InlineData("relative.pdf")]
    [InlineData("./relative.pdf")]
    [InlineData("../relative.pdf")]
    [InlineData("folder/relative.pdf")]
    public async Task ExplicitWebBaseMakesSimpleRelativeBookmarkExternal(string relative)
    {
        var result = await Run(await Fixture(Build(relative, "base")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.True(facts.Basics.HasBookmarks);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("Web", Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData("<< /Base (http://base.invalid/folder/) >>", CheckStatus.Fail)]
    [InlineData("<< /Base (https://base.invalid/document.pdf) >>", CheckStatus.Fail)]
    [InlineData("null", CheckStatus.NotEvaluated)]
    [InlineData("42", CheckStatus.NotEvaluated)]
    [InlineData("<< >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base null >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base /Name >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base 99 0 R >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (relative/) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (file:///C:/folder/) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (ftp://base.invalid/) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (https://) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (https://base.invalid/%ZZ/) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (https://base.invalid/?q=1) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (https://base.invalid/#part) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /Base (https://synthetic-user@base.invalid/) >>", CheckStatus.NotEvaluated)]
    public async Task RelativeBookmarkRequiresExplicitQualifiedWebBase(string entry, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("relative.pdf", baseEntry: entry)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status); Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("#part")]
    [InlineData("?query=1")]
    [InlineData("relative%2Epdf")]
    [InlineData("relative.pdf#part")]
    [InlineData("relative.pdf?q=1")]
    [InlineData("relative file.pdf")]
    [InlineData("/relative.pdf")]
    [InlineData("//other.invalid/relative.pdf")]
    [InlineData("C:/relative.pdf")]
    public async Task AmbiguousRelativeFormsAreNotQualifiedByBaseAlone(string relative)
    {
        var result = await Run(await Fixture(Build(relative, "base")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Empty(Assert.Single(result.Documents).Facts!.ExternalBookmarks);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("<< /Base 99 0 R >>")]
    [InlineData("<< /Base (file:///C:/folder/) >>")]
    public async Task InvalidBaseDoesNotInvalidateAnExplicitAbsoluteWebBookmark(string entry)
    {
        var result = await Run(await Fixture(Build("https://example.invalid/document", baseEntry: entry)));
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("wrong-parent")]
    [InlineData("conflict")]
    [InlineData("next-action")]
    [InlineData("wrong-type")]
    [InlineData("is-map")]
    public async Task WebBaseDoesNotBypassBookmarkActionQualification(string scenario)
    {
        var result = await Run(await Fixture(Build("relative.pdf", scenario, "<< /Base (https://base.invalid/) >>")));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task BaseRelativeFindingPreservesAllowanceCancellationAndPrivacy(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("synthetic-relative.pdf", baseEntry: "<< /Base (https://synthetic-base.invalid/folder/) >>"));
        var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfExternalBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); Assert.Equal(expected, run.Finish(true)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfExternalBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(document, 1));
        Assert.Equal("Web", Assert.Single(retained!.Findings).Actual);
        Assert.DoesNotContain("synthetic-", System.Text.Json.JsonSerializer.Serialize(retained));
        Assert.DoesNotContain("synthetic-", System.Text.Json.JsonSerializer.Serialize(document.Facts!.ExternalBookmarks));
    }

    [Fact]
    public async Task BaseRelativeFindingRemainsPartialInReport()
    {
        var fixture = await Fixture(Build("relative.pdf", "base"));
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("https://example.invalid/document", "bookmark", CheckStatus.Fail)]
    [InlineData("http://example.invalid/", "bookmark", CheckStatus.Fail)]
    [InlineData("HTTPS://example.invalid/", "bookmark", CheckStatus.Fail)]
    [InlineData("mailto:review@example.invalid", "bookmark", CheckStatus.Fail)]
    [InlineData("https://example.invalid/document", "base", CheckStatus.Fail)]
    [InlineData("relative.pdf", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("relative.pdf", "base", CheckStatus.Fail)]
    [InlineData("#page=1", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("https://", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("mailto:", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("ftp://example.invalid/", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("https%3A//example.invalid/", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData(" https://example.invalid/", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "page-link", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "open-action", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "wrong-parent", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "conflict", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "next-action", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "wrong-type", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "is-map", CheckStatus.NotEvaluated)]
    public async Task ExternalUriRequiresQualifiedBookmarkAndAbsoluteSupportedAddress(string uri, string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(uri, scenario)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
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
        var result = await Run(await Fixture(Build("https://example.invalid/"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task RegulatoryFindingRetainsCategoryNotOriginalAddress()
    {
        const string uri = "https://synthetic-user@example.invalid/path?synthetic-query=example";
        var result = await Run(await Fixture(Build(uri)));
        var finding = Assert.Single(Assert.Single(result.Checks, item => item.RuleId == RuleId).Findings);
        Assert.Equal("Web", finding.Actual);
        Assert.DoesNotContain("synthetic-", System.Text.Json.JsonSerializer.Serialize(finding));
        var observation = Assert.Single(Assert.Single(result.Documents).Facts!.ExternalBookmarks);
        Assert.Equal(PdfExternalBookmarkKind.Web, observation.Kind);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("no-bookmarks")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("unknown-kind")]
    [InlineData("invalid-kind")]
    [InlineData("label-only")]
    public async Task UnboundOrContradictoryFactsDoNotProveExternalBookmark(string scenario)
    {
        var fixture = await Fixture(Build("https://example.invalid/"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], scenario != "no-bookmarks", [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("outline[1]/URI", null, new(Uri: "https://example.invalid/"))], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            externalBookmarks: scenario == "label-only" ? [] : [new("outline[1]/A/URI", scenario == "unknown-kind" ? PdfExternalBookmarkKind.Unknown : scenario == "invalid-kind" ? (PdfExternalBookmarkKind)99 : PdfExternalBookmarkKind.Web)]);
        var run = new FdaPdfExternalBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("https://example.invalid/")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfExternalBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("https://example.invalid/")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfExternalBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
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
        var result = await Run(await Fixture(Build("https://example.invalid/"), limits: new() { MaxXmlFindings = limit }));
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
        var result = await Run(await Fixture(Build("https://example.invalid/"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialCoverageRetainsLocatedFailure()
    {
        var fixture = await Fixture(Build("https://example.invalid/"));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, string? date = "2026-11-25", PackageReadLimits? limits = null, bool missingSource = false)
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
        public string SourceId => "fda5105";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string uri, string scenario = "bookmark", string? baseEntry = null)
    {
        var action = "<< /S /URI /URI (" + uri + ")" + (scenario == "wrong-type" ? " /Type /Bogus" : "") +
            (scenario == "next-action" ? " /Next << /S /URI /URI (https://example.invalid/other) >>" : "") + (scenario == "is-map" ? " /IsMap true" : "") + " >>";
        if (scenario == "page-link") return PdfPigPackageInspectorTests.Build("/A " + action);
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R" + (baseEntry is not null ? " /URI " + baseEntry : scenario == "base" ? " /URI << /Base (https://base.invalid/) >>" : ""),
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
            "<< /Title (bookmark) /Parent " + (scenario == "wrong-parent" ? "1" : "7") + " 0 R /A " + action + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "") + " >>");
    }
}
