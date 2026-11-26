using System.Globalization;
using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfInactiveHyperlinkRuleTests
{
    private const string RuleId = "FDA47-5210";

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task StructureParentIndexDoesNotActivateAnOtherwiseInactiveLink(int index)
    {
        var result = await Run(await Fixture(PdfPigPackageInspectorTests.Build("/StructParent " + index.ToString(CultureInfo.InvariantCulture))));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("NoActionOrDestination", Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("(0)")]
    [InlineData("/Zero")]
    [InlineData("[]")]
    [InlineData("<< >>")]
    [InlineData("7 0 R")]
    public async Task UnsupportedStructureIndexCannotQualifyInactivity(string value)
    {
        var pdf = PdfPigPackageInspectorTests.Build("/StructParent " + value, "", "0");
        var result = await Run(await Fixture(pdf));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Empty(facts.InactiveHyperlinks);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("/A null")]
    [InlineData("/Dest null")]
    [InlineData("/AA << >>")]
    [InlineData("/A << /S /Unknown >>")]
    [InlineData("/A << /S /GoTo /D [3 0 R /Fit] >>")]
    [InlineData("/Dest [3 0 R /Fit]")]
    [InlineData("/F 2")]
    [InlineData("/P 2 0 R")]
    [InlineData("/AP << >>")]
    public async Task StructureIndexDoesNotBypassActionOrAnnotationBoundaries(string properties)
    {
        var result = await Run(await Fixture(PdfPigPackageInspectorTests.Build("/StructParent 0 " + properties)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Empty(facts.InactiveHyperlinks);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
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
    [InlineData("structure", CheckStatus.Fail)]
    [InlineData("hidden", CheckStatus.NotEvaluated)]
    [InlineData("wrong-page", CheckStatus.NotEvaluated)]
    [InlineData("appearance", CheckStatus.NotEvaluated)]
    [InlineData("bookmark", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    public async Task InactiveLinkRequiresExplicitAbsenceNotUnresolvedAction(string scenario, CheckStatus expected)
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
            Assert.Contains("page[1]", finding.Location.FieldPath);
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
    [InlineData("inactive", 1)]
    [InlineData("duplicate", 0)]
    [InlineData("unknown-pages", 0)]
    [InlineData("budget", 0)]
    [InlineData("io", 0)]
    [InlineData("not-link", 0)]
    [InlineData("null-action", 0)]
    public void ReaderRequiresUniqueQualifiedLinkAndRawKeyAbsence(string scenario, int expected)
    {
        var reference = new IndirectReferenceToken(new(6, 0));
        var entries = new Dictionary<NameToken, IToken> { [NameToken.Create("Subtype")] = NameToken.Create(scenario == "not-link" ? "Text" : "Link"),
            [NameToken.Create("Rect")] = new ArrayToken([new NumericToken(0), new NumericToken(0), new NumericToken(100), new NumericToken(100)]) };
        if (scenario == "null-action") entries[NameToken.Create("A")] = NullToken.Instance;
        var annotation = new DictionaryToken(entries);
        var page = new DictionaryToken(new Dictionary<NameToken, IToken> { [NameToken.Create("Annots")] = new ArrayToken(scenario == "duplicate" ? [reference, reference] : [reference]) });
        IToken? Resolve(IToken? value) => value is not IndirectReferenceToken ? value : scenario == "io" ? throw new IOException("opaque") : annotation;
        var reader = new PdfPigPackageHyperlinkReader(Resolve, new() { MaxPdfObjects = scenario == "budget" ? 1 : 500 }, CancellationToken.None);
        _ = reader.Read([page], new Dictionary<IndirectReference, int> { [new(3, 0)] = 1 }, scenario == "unknown-pages" ? null : 1);
        Assert.Equal(expected, reader.InactiveHyperlinks.Count);
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
    [InlineData("label-only")]
    public async Task LegacyOrContradictoryFactsCannotProveInactiveLink(string scenario)
    {
        var fixture = await Fixture(Build("inactive"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("page[1]", 1, new())], [], false, false,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            qualifiedPageCount: scenario == "unqualified-pages" ? null : scenario == "page-conflict" ? 2 : 1,
            inactiveHyperlinks: scenario == "label-only" ? [] : [new("page[1]/annotation[1]", scenario == "wrong-page" ? 2 : 1)]);
        var run = new FdaPdfInactiveHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, false)]
    [InlineData(1, CheckStatus.Fail, false)]
    [InlineData(0, CheckStatus.NotEvaluated, true)]
    [InlineData(1, CheckStatus.Fail, true)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected, bool structure)
    {
        var fixture = await Fixture(Build(structure ? "structure" : "inactive")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfInactiveHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure, bool structure)
    {
        var fixture = await Fixture(Build(structure ? "structure" : "inactive")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfInactiveHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(3, CheckStatus.NotEvaluated)]
    [InlineData(4, CheckStatus.Fail)]
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialCoverageRetainsLocatedFailure(bool structure)
    {
        var fixture = await Fixture(Build(structure ? "structure" : "inactive"));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
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
        public string SourceId => "fda5210";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string scenario)
    {
        const string destination = "/Dest [3 0 R /Fit]";
        if (scenario == "bookmark") return PdfPigPackageInspectorTests.Build(destination, "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (bookmark) /Parent 7 0 R >>");
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build(destination, "/OpenAction << >>");
        var properties = scenario switch {
            "dest" => destination, "action" => "/A << /S /GoTo /D [3 0 R /Fit] >>", "null-action" => "/A null", "null-dest" => "/Dest null",
            "unresolved" => "/A 99 0 R", "unknown-action" => "/A << /S /Unknown >>", "aa" => "/AA << >>", "structure" => "/StructParent 0",
            "hidden" => "/F 2", "wrong-page" => "/P 2 0 R", "appearance" => "/AP << >>", _ => "" };
        return PdfPigPackageInspectorTests.Build(properties);
    }
}
