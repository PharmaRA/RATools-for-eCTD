using System.Globalization;
using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfBookmarkZoomRuleTests
{
    private const string RuleId = "FDA47-5117";

    [Theory]
    [InlineData("[0 /Fit]")]
    [InlineData("[0 /XYZ null null 1.25]")]
    public async Task QualifiedFileDictionaryKeepsExplicitBookmarkZoom(string destination)
    {
        var result = await Run(await Fixture(Build(destination, "file-dictionary")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Single(facts.BookmarkFiles);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("outline[1]/A/D", Assert.Single(check.Findings).Location.FieldPath);
    }

    [Theory]
    [InlineData("<< /Type /Filespec /F (target.pdf) >>", CheckStatus.Fail)]
    [InlineData("<< /F (target.pdf) /UF (target.pdf) >>", CheckStatus.Fail)]
    [InlineData("<< /F <7461726765742E706466> >>", CheckStatus.Fail)]
    [InlineData("<< /F (target.pdf) /UF (other.pdf) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /UF null >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /UF 99 0 R >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /Type /Other >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F null >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F 99 0 R >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /UF (target.pdf) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /FS /URL >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /EF << >> >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /DOS (target.pdf) >>", CheckStatus.NotEvaluated)]
    [InlineData("<< /F (target.pdf) /V false >>", CheckStatus.NotEvaluated)]
    public async Task FileDictionaryMustHaveUnambiguousQualifiedIdentity(string specification, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("[0 /XYZ null null 1]", specification: specification)));
        var facts = Assert.Single(result.Documents).Facts!;
        if (specification.Contains("99 0 R", StringComparison.Ordinal)) Assert.NotNull(facts.Basics.ParseError);
        else Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, facts.NonInheritingBookmarkZooms.Count);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("<< /F (target.pdf) >>", 1)]
    [InlineData("<< /F (target.pdf) /UF 99 0 R >>", 0)]
    [InlineData("<< /F 99 0 R >>", 0)]
    public void SupplementalReaderDoesNotTreatUnresolvedFileIdentityAsQualified(string specification, int count)
    {
        using var stream = new MemoryStream(Build("[0 /XYZ null null 1]", specification: specification));
        using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false });
        IToken? Resolve(IToken? value) => value is not IndirectReferenceToken reference ? value :
            reference.Data == new UglyToad.PdfPig.Core.IndirectReference(99, 0) ? null : document.Structure.GetObject(reference.Data)?.Data;
        var reader = new PdfPigPackageBookmarkReader(Resolve, new(), CancellationToken.None);
        reader.Read(document.Structure.Catalog.CatalogDictionary);
        Assert.Equal(count, reader.NonInheritingZooms.Count);
    }

    [Theory]
    [InlineData("[0 /XYZ null null 0]", "bookmark")]
    [InlineData("[0 /XYZ null null null]", "bookmark")]
    [InlineData("[0 /XYZ null null -1]", "bookmark")]
    [InlineData("[0 /XYZ /bad null 1]", "bookmark")]
    [InlineData("[0 /Fit 1]", "bookmark")]
    [InlineData("[0 /Fit]", "internal")]
    [InlineData("[0 /Fit]", "page-link")]
    [InlineData("[0 /Fit]", "open-action")]
    [InlineData("[0 /Fit]", "next-action")]
    [InlineData("[0 /Fit]", "conflict")]
    [InlineData("[0 /Fit]", "bad-parent")]
    public async Task FileDictionaryDoesNotBypassDestinationAndBookmarkQualification(string destination, string scenario)
    {
        var result = await Run(await Fixture(Build(destination, scenario, "<< /F (target.pdf) >>")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Empty(Assert.Single(result.Documents).Facts!.NonInheritingBookmarkZooms);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task DictionaryZoomHonorsAllowanceAndCancellation(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("[0 /Fit]", "file-dictionary")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); Assert.Equal(expected, run.Finish(true)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(document, 1));
        Assert.Equal(CheckStatus.Fail, retained!.Status); Assert.Single(retained.Findings);
    }

    [Fact]
    public async Task DictionaryZoomFailureRemainsPartialInReport()
    {
        var fixture = await Fixture(Build("[0 /Fit]", "file-dictionary"));
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("[0 /Fit]", "bookmark", CheckStatus.Fail)]
    [InlineData("[0 /XYZ null null 1]", "bookmark", CheckStatus.Fail)]
    [InlineData("[0 /XYZ 0 10 1.5]", "bookmark", CheckStatus.Fail)]
    [InlineData("[0 /XYZ null null 0]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null null]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null -1]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null (1)]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ /bad null 1]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit 1]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /FitH null]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("(named)", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[-1 /Fit]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0.5 /Fit]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "page-link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "open-action", CheckStatus.NotEvaluated)]
    [InlineData("[3 0 R /Fit]", "internal", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "next-action", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "conflict", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "bad-parent", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "wrong-type", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "no-file", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "file-dictionary", CheckStatus.Fail)]
    public async Task ExplicitZoomSettingIsDistinctFromInheritedOrUnknownSetting(string destination, string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(destination, scenario)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Equal("Ensure that all bookmarks are set to 'Inherit Zoom'.", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-20", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-21", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task PolicyQualificationIsDateBound(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("[0 /Fit]"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("[0 /Fit]", PdfBookmarkZoomKind.FitPage)]
    [InlineData("[0 /XYZ null null 2]", PdfBookmarkZoomKind.FixedFactor)]
    [InlineData("[0 /XYZ null null 0]", PdfBookmarkZoomKind.Unknown)]
    [InlineData("[0 /XYZ null null null]", PdfBookmarkZoomKind.Unknown)]
    [InlineData("[0 /XYZ /bad null 2]", PdfBookmarkZoomKind.Unknown)]
    public void AdapterRetainsOnlyQualifiedNonInheritingSettings(string destination, PdfBookmarkZoomKind expected)
    {
        using var stream = new MemoryStream(Build(destination));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        if (expected == PdfBookmarkZoomKind.Unknown) Assert.Empty(facts.NonInheritingBookmarkZooms);
        else Assert.Equal(expected, Assert.Single(facts.NonInheritingBookmarkZooms).Kind);
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
    [InlineData("no-independent-fact")]
    public async Task UnboundOrContradictoryFactsCannotProveNonInheritedZoom(string scenario)
    {
        var fixture = await Fixture(Build("[0 /Fit]"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], scenario != "no-bookmarks", [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            nonInheritingBookmarkZooms: scenario == "no-independent-fact" ? [] : [new("outline[1]/A/D", scenario == "unknown-kind" ? PdfBookmarkZoomKind.Unknown : scenario == "invalid-kind" ? (PdfBookmarkZoomKind)99 : PdfBookmarkZoomKind.FitPage)]);
        var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("[0 /XYZ null null 1]")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("[0 /XYZ null null 1]")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(4, CheckStatus.NotEvaluated)]
    [InlineData(5, CheckStatus.NotEvaluated)]
    [InlineData(6, CheckStatus.Fail)]
    public async Task SharedBudgetOnlyRetainsAllocatedFindings(int limit, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("[0 /XYZ null null 1]"), limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(limit >= 5 ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            Assert.Single(result.Checks, item => item.RuleId == "FDA47-5100").Status);
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

    [Theory]
    [InlineData("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c")]
    [InlineData("113faa128d34ca5c2b99184da2cf4da2e4359403204289892720c1f73afcae2a")]
    public async Task BothArchivedSourcesAreRequired(string missingHash)
    {
        var result = await Run(await Fixture(Build("[0 /Fit]"), missingHash: missingHash));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialReportRetainsLocatedFailure()
    {
        var fixture = await Fixture(Build("[0 /Fit]"));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConflictingZoomKindsCannotAuthorizeFinding(bool fitFirst, bool independent)
    {
        var fixture = await Fixture(Build("[0 /Fit]"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var fit = new PdfBookmarkZoom("outline[1]/A/D", PdfBookmarkZoomKind.FitPage);
        var unknown = fit with { Kind = PdfBookmarkZoomKind.Unknown };
        var zooms = fitFirst ? new List<PdfBookmarkZoom> { fit, unknown } : [unknown, fit];
        if (independent) zooms.Add(new("outline[2]/A/D", PdfBookmarkZoomKind.FixedFactor));
        var basics = new PdfInspectionResult("1.4", false, false, true, true, [], true, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed, nonInheritingBookmarkZooms: zooms);
        var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(independent ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(independent ? 1 : 0, check.Findings.Count);
        if (independent)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("outline[2]/A/D", finding.Location.FieldPath);
            Assert.Equal("FixedFactor", finding.Actual);
        }
        foreach (var remaining in new[] { 0, 1 })
        {
            var bounded = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
            bounded.Observe(new(file.LogicalPath, facts, file.Sha256), remaining);
            Assert.Equal(independent ? remaining : 0, bounded.FindingCount);
        }
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("outline[1]/A/D", 0)]
    [InlineData("outline[2]/A/D", 2)]
    public async Task DuplicateOrUnlocatedZoomsDoNotAuthorizeExtraFindings(string? otherPath, int expected)
    {
        var fixture = await Fixture(Build("[0 /Fit]"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", false, false, true, true, [], true, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
            nonInheritingBookmarkZooms: [new(otherPath!, PdfBookmarkZoomKind.FitPage), new("outline[1]/A/D", PdfBookmarkZoomKind.FitPage)]);
        var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        Assert.Equal(expected, run.FindingCount);
        Assert.Equal(expected > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, run.Finish(false)!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteZoomUniquenessScanCannotPublish(bool conflictLast)
    {
        var baseline = await Fixture(Build("[0 /Fit]"));
        var fixture = await Fixture(Build("[0 /Fit]"), limits: new PackageReadLimits { MaxPdfObjects = baseline.Input.Packages.Target.Manifest.Files.Count + 3 });
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", false, false, true, true, [], true, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
            nonInheritingBookmarkZooms: [new("outline[1]/A/D", PdfBookmarkZoomKind.FitPage), new("outline[2]/A/D", PdfBookmarkZoomKind.FixedFactor),
                new(conflictLast ? "outline[1]/A/D" : "outline[3]/A/D", PdfBookmarkZoomKind.Unknown)]);
        var run = new FdaPdfBookmarkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, string? date = "2026-11-26", PackageReadLimits? limits = null, string? missingHash = null)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingHash is not null)
        {
            Assert.Contains(snapshot.Resources, item => item.Sha256 == missingHash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != missingHash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != missingHash), snapshot.AcquiredUtc);
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
        public string SourceId => "fda5117";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string destination, string scenario = "bookmark", string? specification = null)
    {
        var file = scenario == "no-file" ? "" : scenario == "file-dictionary" ? " /F << /F (target.pdf) >>" : " /F (target.pdf)";
        if (specification is not null) file = " /F " + specification;
        var action = "<< /S /" + (scenario == "internal" ? "GoTo" : "GoToR") + file + " /D " + destination +
            (scenario == "next-action" ? " /Next << /S /GoToR /F (target.pdf) /D [0 /Fit] >>" : "") + (scenario == "wrong-type" ? " /Type /Bogus" : "") + " >>";
        if (scenario == "page-link") return PdfPigPackageInspectorTests.Build("/A " + action);
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
            "<< /Title (bookmark) /Parent " + (scenario == "bad-parent" ? "1" : "7") + " 0 R /A " + action + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "") + " >>");
    }
}
