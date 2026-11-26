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
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfHyperlinkZoomRuleTests
{
    private const string RuleId = "FDA47-5217";

    [Theory]
    [InlineData("[0 /Fit]")]
    [InlineData("[0 /XYZ null null 1.25]")]
    public async Task QualifiedFileDictionaryKeepsExplicitHyperlinkZoom(string destination)
    {
        var result = await Run(await Fixture(Build(destination, "file-dictionary")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Single(facts.HyperlinkFiles);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("page[1]/annotation[1]/A/D", Assert.Single(check.Findings).Location.FieldPath);
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
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, facts.NonInheritingHyperlinkZooms.Count);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("<< /F (target.pdf) >>", 1)]
    [InlineData("<< /F (target.pdf) /UF 99 0 R >>", 0)]
    [InlineData("<< /F 99 0 R >>", 0)]
    public void SupplementalReaderDoesNotQualifyUnresolvedFileIdentity(string specification, int count)
    {
        using var stream = new MemoryStream(Build("[0 /XYZ null null 1]", specification: specification));
        using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false });
        IToken? Resolve(IToken? value) => value is not IndirectReferenceToken reference ? value :
            reference.Data == new IndirectReference(99, 0) ? null : document.Structure.GetObject(reference.Data)?.Data;
        var page = Assert.IsType<DictionaryToken>(document.Structure.GetObject(new(3, 0))!.Data);
        var reader = new PdfPigPackageHyperlinkReader(Resolve, new(), CancellationToken.None);
        reader.Read([page], new Dictionary<IndirectReference, int> { [new(3, 0)] = 1 }, 1);
        Assert.Equal(count, reader.NonInheritingZooms.Count);
    }

    [Theory]
    [InlineData("[0 /XYZ null null 0]", "link")]
    [InlineData("[0 /XYZ null null null]", "link")]
    [InlineData("[0 /XYZ null null -1]", "link")]
    [InlineData("[0 /XYZ /bad null 1]", "link")]
    [InlineData("[0 /Fit 1]", "link")]
    [InlineData("[0 /Fit]", "internal")]
    [InlineData("[0 /Fit]", "bookmark")]
    [InlineData("[0 /Fit]", "open-action")]
    [InlineData("[0 /Fit]", "next-action")]
    [InlineData("[0 /Fit]", "conflict")]
    [InlineData("[0 /Fit]", "wrong-page")]
    [InlineData("[0 /Fit]", "hidden")]
    public async Task FileDictionaryDoesNotBypassDestinationOrPageLinkQualification(string destination, string scenario)
    {
        var result = await Run(await Fixture(Build(destination, scenario, "<< /F (target.pdf) >>")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Empty(Assert.Single(result.Documents).Facts!.NonInheritingHyperlinkZooms);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("[0 /Fit]", "link", CheckStatus.Fail)]
    [InlineData("[0 /XYZ null null 1]", "link", CheckStatus.Fail)]
    [InlineData("[0 /XYZ 0 10 1.5]", "link", CheckStatus.Fail)]
    [InlineData("[0 /XYZ null null 0]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null null]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null -1]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ null null (1)]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /XYZ /bad null 1]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit 1]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /FitH null]", "link", CheckStatus.NotEvaluated)]
    [InlineData("(named)", "link", CheckStatus.NotEvaluated)]
    [InlineData("[-1 /Fit]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0.5 /Fit]", "link", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "open-action", CheckStatus.NotEvaluated)]
    [InlineData("[3 0 R /Fit]", "internal", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "next-action", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "conflict", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "wrong-page", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "hidden", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "wrong-type", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "no-file", CheckStatus.NotEvaluated)]
    [InlineData("[0 /Fit]", "file-dictionary", CheckStatus.Fail)]
    public async Task ExplicitZoomIsDistinctFromInheritedOrUnknownSetting(string destination, string scenario, CheckStatus expected)
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
            Assert.Contains("page[1]", finding.Location.FieldPath);
            Assert.Equal("Ensure that all links in future submissions are set to 'Inherit Zoom'.", finding.RecommendedAction);
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
    [InlineData("[0 /Fit]", PdfHyperlinkZoomKind.FitPage)]
    [InlineData("[0 /XYZ null null 2]", PdfHyperlinkZoomKind.FixedFactor)]
    [InlineData("[0 /XYZ null null 0]", PdfHyperlinkZoomKind.Unknown)]
    [InlineData("[0 /XYZ null null null]", PdfHyperlinkZoomKind.Unknown)]
    [InlineData("[0 /XYZ /bad null 2]", PdfHyperlinkZoomKind.Unknown)]
    public void AdapterRetainsOnlyQualifiedNonInheritingSettings(string destination, PdfHyperlinkZoomKind expected)
    {
        using var stream = new MemoryStream(Build(destination));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        if (expected == PdfHyperlinkZoomKind.Unknown) Assert.Empty(facts.NonInheritingHyperlinkZooms);
        else Assert.Equal(expected, Assert.Single(facts.NonInheritingHyperlinkZooms).Kind);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("unqualified-pages")]
    [InlineData("page-conflict")]
    [InlineData("wrong-page")]
    [InlineData("unknown-kind")]
    [InlineData("invalid-kind")]
    [InlineData("no-independent-fact")]
    public async Task UnboundOrContradictoryFactsCannotProveNonInheritedZoom(string scenario)
    {
        var fixture = await Fixture(Build("[0 /Fit]"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            qualifiedPageCount: scenario == "unqualified-pages" ? null : scenario == "page-conflict" ? 2 : 1,
            nonInheritingHyperlinkZooms: scenario == "no-independent-fact" ? [] : [new("page[1]/annotation[1]/A/D", scenario == "wrong-page" ? 2 : 1,
                scenario == "unknown-kind" ? PdfHyperlinkZoomKind.Unknown : scenario == "invalid-kind" ? (PdfHyperlinkZoomKind)99 : PdfHyperlinkZoomKind.FitPage)]);
        var run = new FdaPdfHyperlinkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, false)]
    [InlineData(1, CheckStatus.Fail, false)]
    [InlineData(0, CheckStatus.NotEvaluated, true)]
    [InlineData(1, CheckStatus.Fail, true)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected, bool dictionary)
    {
        var fixture = await Fixture(Build("[0 /XYZ null null 1]", dictionary ? "file-dictionary" : "link")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfHyperlinkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure, bool dictionary)
    {
        var fixture = await Fixture(Build("[0 /XYZ null null 1]", dictionary ? "file-dictionary" : "link")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfHyperlinkZoomRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(3, CheckStatus.NotEvaluated)]
    [InlineData(4, CheckStatus.NotEvaluated)]
    [InlineData(5, CheckStatus.Fail)]
    public async Task SharedBudgetOnlyRetainsAllocatedFindings(int limit, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("[0 /XYZ null null 1]"), limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        if (limit >= 4)
        {
            var missing = Assert.Single(result.Checks, item => item.RuleId == "FDA47-5200");
            Assert.Equal(CheckStatus.Fail, missing.Status); Assert.Single(missing.Findings);
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialReportRetainsLocatedFailure(bool dictionary)
    {
        var fixture = await Fixture(Build("[0 /Fit]", dictionary ? "file-dictionary" : "link"));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
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
        public string SourceId => "fda5217";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string destination, string scenario = "link", string? specification = null)
    {
        var file = specification is not null ? " /F " + specification : scenario == "no-file" ? "" : scenario == "file-dictionary" ? " /F << /F (target.pdf) >>" : " /F (target.pdf)";
        var action = "<< /S /" + (scenario == "internal" ? "GoTo" : "GoToR") + file + " /D " + destination +
            (scenario == "next-action" ? " /Next << /S /GoToR /F (target.pdf) /D [0 /Fit] >>" : "") + (scenario == "wrong-type" ? " /Type /Bogus" : "") + " >>";
        if (scenario == "bookmark") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (bookmark) /Parent 7 0 R /A " + action + " >>");
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/A " + action + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "") +
            (scenario == "wrong-page" ? " /P 2 0 R" : "") + (scenario == "hidden" ? " /F 2" : ""));
    }
}
