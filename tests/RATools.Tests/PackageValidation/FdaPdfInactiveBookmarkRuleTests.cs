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

public sealed class FdaPdfInactiveBookmarkRuleTests
{
    private const string RuleId = "FDA47-5110";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupplementalReadFailureCannotConsumeCancellation(bool io)
    {
        using var stream = new MemoryStream(Build("inactive"));
        using var document = UglyToad.PdfPig.PdfDocument.Open(stream);
        using var cancellation = new CancellationTokenSource();
        var called = false;
        var reader = new PdfPigPackageBookmarkReader(_ =>
        {
            called = true;
            cancellation.Cancel();
            throw io ? new IOException("Injected outline failure.") : new InvalidOperationException("Injected outline failure.");
        }, new(), cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => reader.Read(document.Structure.Catalog.CatalogDictionary));
        Assert.True(called);
        Assert.Empty(reader.InactiveBookmarks);
    }

    [Fact]
    public void PreCancelledSupplementalReaderRejectsEvenAbsentOutlines()
    {
        using var stream = new MemoryStream(Build("no-outlines"));
        using var document = UglyToad.PdfPig.PdfDocument.Open(stream);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var called = false;
        var reader = new PdfPigPackageBookmarkReader(value => { called = true; return value; }, new(), cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => reader.Read(document.Structure.Catalog.CatalogDictionary));
        Assert.False(called);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("ordinary")]
    [InlineData("limit")]
    [InlineData("changed")]
    [InlineData("cancelled")]
    public void SupplementalFailuresDoNotProveInactiveBookmarks(string category)
    {
        using var stream = new MemoryStream(Build("inactive"));
        using var document = UglyToad.PdfPig.PdfDocument.Open(stream);
        Exception failure = category switch
        {
            "io" => new IOException("Injected unavailable outline evidence."),
            "limit" => new PdfInspectionLimitException("Injected supplemental limit."),
            "changed" => new PackageInputChangedException("inactive-bookmark-fixture"),
            "cancelled" => new OperationCanceledException("Injected cancellation."),
            _ => new InvalidOperationException("Injected unavailable outline evidence.")
        };
        var calls = 0;
        var reader = new PdfPigPackageBookmarkReader(_ => { calls++; throw failure; }, new(), CancellationToken.None);
        if (category is "changed" or "cancelled") Assert.Same(failure, Record.Exception(() => reader.Read(document.Structure.Catalog.CatalogDictionary)));
        else Assert.Empty(reader.Read(document.Structure.Catalog.CatalogDictionary));
        Assert.Equal(1, calls);
        Assert.Empty(reader.InactiveBookmarks);
        Assert.Empty(reader.ExternalBookmarks);
        Assert.Empty(reader.MultipleActions);
        Assert.Empty(reader.NonInheritingZooms);
    }

    [Fact]
    public async Task ExplicitZeroCountDoesNotActivateAChildlessBookmark()
    {
        var result = await Run(await Fixture(Build("count-zero")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.True(facts.Basics.HasBookmarks);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("outline[1]", Assert.Single(check.Findings).Location.FieldPath);
    }

    [Theory]
    [InlineData("/Count 0", CheckStatus.Fail)]
    [InlineData("/Count +0", CheckStatus.Fail)]
    [InlineData("/Count -0", CheckStatus.Fail)]
    [InlineData("/Count 1", CheckStatus.NotEvaluated)]
    [InlineData("/Count -1", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0.5", CheckStatus.NotEvaluated)]
    [InlineData("/Count null", CheckStatus.NotEvaluated)]
    [InlineData("/Count (0)", CheckStatus.NotEvaluated)]
    [InlineData("/Count /Zero", CheckStatus.NotEvaluated)]
    [InlineData("/Count 9 0 R", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /A null", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /Dest null", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /Dest [3 0 R /Fit]", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /AA << >>", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /SE 9 0 R", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /First null /Last null", CheckStatus.NotEvaluated)]
    [InlineData("/Count 0 /First 9 0 R /Last 9 0 R", CheckStatus.NotEvaluated)]
    public async Task ZeroCountCannotReplaceActionAndChildAbsence(string properties, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("inactive", properties)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status); Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("bad-parent")]
    [InlineData("no-title")]
    [InlineData("bad-last")]
    public async Task ZeroCountDoesNotBypassCompleteOutlineIdentity(string scenario)
    {
        var result = await Run(await Fixture(Build(scenario, "/Count 0")));
        Assert.Empty(Assert.Single(result.Documents).Facts!.InactiveBookmarks);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ZeroCountHonorsAllowanceAndRetainsCancelledFailure(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("count-zero")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfInactiveBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); Assert.Equal(expected, run.Finish(true)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfInactiveBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(document, 1));
        Assert.Equal(CheckStatus.Fail, retained!.Status); Assert.Single(retained.Findings);
    }

    [Fact]
    public async Task ZeroCountFailureRemainsPartialInReport()
    {
        var fixture = await Fixture(Build("count-zero"));
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("inactive", CheckStatus.Fail)]
    [InlineData("dest", CheckStatus.NotEvaluated)]
    [InlineData("action", CheckStatus.NotEvaluated)]
    [InlineData("null-action", CheckStatus.NotEvaluated)]
    [InlineData("null-dest", CheckStatus.NotEvaluated)]
    [InlineData("unresolved", CheckStatus.NotEvaluated)]
    [InlineData("unknown-action", CheckStatus.NotEvaluated)]
    [InlineData("aa", CheckStatus.NotEvaluated)]
    [InlineData("structure", CheckStatus.NotEvaluated)]
    [InlineData("parent", CheckStatus.NotEvaluated)]
    [InlineData("bad-parent", CheckStatus.NotEvaluated)]
    [InlineData("no-title", CheckStatus.NotEvaluated)]
    [InlineData("bad-last", CheckStatus.NotEvaluated)]
    [InlineData("partial-children", CheckStatus.NotEvaluated)]
    [InlineData("no-outlines", CheckStatus.NotEvaluated)]
    [InlineData("page-annotation", CheckStatus.NotEvaluated)]
    [InlineData("count-zero", CheckStatus.Fail)]
    [InlineData("null-children", CheckStatus.NotEvaluated)]
    public async Task InactiveLeafRequiresExplicitAbsenceNotUnresolvedAction(string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(scenario)));
        var error = Assert.Single(result.Documents).Facts!.Basics.ParseError;
        if (scenario == "unresolved") Assert.NotNull(error);
        else Assert.Null(error);
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
        var result = await Run(await Fixture(Build("inactive"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("no-bookmarks")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("no-independent-fact")]
    public async Task UnboundOrContradictoryFactsCannotProveInactivity(string scenario)
    {
        var fixture = await Fixture(Build("inactive"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], scenario != "no-bookmarks", [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            inactiveBookmarks: scenario == "no-independent-fact" ? [] : [new("outline[1]")]);
        var run = new FdaPdfInactiveBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceAndRepeatedObservation(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("inactive")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfInactiveBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("inactive")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfInactiveBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
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
        var result = await Run(await Fixture(Build("inactive"), limits: new() { MaxXmlFindings = limit }));
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
        var result = await Run(await Fixture(Build("inactive"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialReportRetainsLocatedFailure()
    {
        var fixture = await Fixture(Build("inactive"));
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
        public string SourceId => "fda5110";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string scenario, string? overrideProperties = null)
    {
        const string destination = "/Dest [3 0 R /Fit]";
        if (scenario is "no-outlines" or "page-annotation") return PdfPigPackageInspectorTests.Build(scenario == "page-annotation" ? "" : destination);
        var properties = scenario switch {
            "dest" => destination, "action" => "/A << /S /GoTo /D [3 0 R /Fit] >>", "null-action" => "/A null", "null-dest" => "/Dest null",
            "unresolved" => "/A 99 0 R", "unknown-action" => "/A << /S /Unknown >>", "aa" => "/AA << >>", "structure" => "/SE 9 0 R",
            "count-zero" => "/Count 0", "null-children" => "/First null /Last null",
            "parent" => "/First 9 0 R /Last 9 0 R /Count 1", "partial-children" => "/First 9 0 R", _ => "" };
        if (overrideProperties is not null) properties = overrideProperties;
        return PdfPigPackageInspectorTests.Build(destination, "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last " + (scenario == "bad-last" ? "9" : "8") + " 0 R /Count " + (scenario == "parent" ? "2" : "1") + " >>",
            "<< " + (scenario == "no-title" ? "" : "/Title (bookmark)") + " /Parent " + (scenario == "bad-parent" ? "1" : "7") + " 0 R " + properties + " >>",
            "<< /Title (child) /Parent 8 0 R " + destination + " >>");
    }
}
