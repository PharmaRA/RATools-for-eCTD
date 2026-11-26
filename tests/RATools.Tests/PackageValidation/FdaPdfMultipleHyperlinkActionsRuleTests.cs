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

public sealed class FdaPdfMultipleHyperlinkActionsRuleTests
{
    private const string RuleId = "FDA47-5203";

    [Theory]
    [InlineData("dictionary", "[0 /XYZ null null null]", "[0 /Fit]", "2")]
    [InlineData("array", "[0 /Fit]", "[0 /XYZ 0 100 0]", "2")]
    [InlineData("nested", "[0 /XYZ null null 1.25]", "[0 /XYZ null null null]", "3")]
    [InlineData("indirect", "[0 /XYZ null null null]", "[0 /XYZ null null null]", "2")]
    [InlineData("three", "[0 /XYZ null null null]", "[0 /XYZ null null 1.25]", "3")]
    public async Task QualifiedXyzHyperlinkChainRetainsAllActions(string scenario, string destination, string nextDestination, string count)
    {
        var result = await Run(await Fixture(Build(scenario, destination, nextDestination)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(count, Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData("[-1 /XYZ null null null]")]
    [InlineData("[0.5 /XYZ null null null]")]
    [InlineData("[3 0 R /XYZ null null null]")]
    [InlineData("[0 /XYZ (0) null null]")]
    [InlineData("[0 /XYZ null /Top null]")]
    [InlineData("[0 /XYZ null null -1]")]
    [InlineData("[0 /XYZ null null (1)]")]
    [InlineData("[0 /XYZ null null]")]
    [InlineData("[0 /XYZ null null null 0]")]
    public async Task InvalidXyzTailDoesNotPublishPartialHyperlinkChain(string destination)
    {
        var result = await Run(await Fixture(Build("dictionary", "[0 /XYZ null null null]", destination)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Empty(facts.MultipleHyperlinkActions);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("empty-array")]
    [InlineData("null")]
    [InlineData("invalid")]
    [InlineData("unknown-action")]
    [InlineData("wrong-type")]
    [InlineData("bad-file")]
    [InlineData("cycle")]
    [InlineData("conflict")]
    [InlineData("hidden")]
    [InlineData("bookmark")]
    [InlineData("open-action")]
    public async Task XyzDoesNotBypassPageIdentityOrCompleteChain(string scenario)
    {
        var result = await Run(await Fixture(Build(scenario, "[0 /XYZ null null null]", "[0 /XYZ null null null]")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Empty(facts.MultipleHyperlinkActions);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("dictionary", CheckStatus.Fail)]
    [InlineData("array", CheckStatus.Fail)]
    [InlineData("three", CheckStatus.Fail)]
    [InlineData("indirect", CheckStatus.Fail)]
    [InlineData("nested", CheckStatus.Fail)]
    [InlineData("single", CheckStatus.NotEvaluated)]
    [InlineData("empty-array", CheckStatus.NotEvaluated)]
    [InlineData("null", CheckStatus.NotEvaluated)]
    [InlineData("invalid", CheckStatus.NotEvaluated)]
    [InlineData("unknown-action", CheckStatus.NotEvaluated)]
    [InlineData("wrong-type", CheckStatus.NotEvaluated)]
    [InlineData("bad-file", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("conflict", CheckStatus.NotEvaluated)]
    [InlineData("hidden", CheckStatus.NotEvaluated)]
    [InlineData("bookmark", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    public async Task MultipleActionsRequireQualifiedHyperlinkAndCompleteChain(string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(scenario)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Contains("page[1]", finding.Location.FieldPath);
            Assert.Equal(scenario is "three" or "nested" ? "3" : "2", finding.Actual);
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
        var result = await Run(await Fixture(Build("dictionary"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("multiple", 2, false)]
    [InlineData("separate", null, false)]
    [InlineData("duplicate", null, false)]
    [InlineData("unknown-pages", null, false)]
    [InlineData("budget", null, false)]
    [InlineData("io", null, false)]
    [InlineData("cycle", null, false)]
    [InlineData("multiple", 2, true)]
    [InlineData("separate", null, true)]
    [InlineData("duplicate", null, true)]
    [InlineData("unknown-pages", null, true)]
    [InlineData("budget", null, true)]
    [InlineData("io", null, true)]
    [InlineData("cycle", null, true)]
    public void ReaderRequiresUniqueLinkAndCompleteInventory(string scenario, int? expected, bool xyz)
    {
        static DictionaryToken Dictionary(params (string Key, IToken Value)[] pairs) => new(pairs.ToDictionary(pair => NameToken.Create(pair.Key), pair => pair.Value));
        var pageReference = new IndirectReference(3, 0); var annotationReference = new IndirectReferenceToken(new(6, 0));
        var otherReference = new IndirectReferenceToken(new(7, 0)); var actionReference = new IndirectReferenceToken(new(8, 0));
        var destination = xyz ? new ArrayToken([new NumericToken(0), NameToken.Create("XYZ"), NullToken.Instance, NullToken.Instance, NullToken.Instance]) :
            new ArrayToken([new NumericToken(0), NameToken.Create("Fit")]);
        var simple = Dictionary(("S", NameToken.Create("GoToR")), ("F", new StringToken("target.pdf")), ("D", destination));
        var multiple = Dictionary(("S", NameToken.Create("GoToR")), ("F", new StringToken("target.pdf")), ("D", destination), ("Next", scenario == "cycle" ? actionReference : simple));
        var annotation = Dictionary(("Subtype", NameToken.Create("Link")), ("Rect", new ArrayToken([new NumericToken(0), new NumericToken(0), new NumericToken(100), new NumericToken(100)])), ("A", scenario == "separate" ? simple : multiple));
        var page = Dictionary(("Annots", new ArrayToken(scenario == "duplicate" ? [annotationReference, annotationReference] : scenario == "separate" ? [annotationReference, otherReference] : [annotationReference])));
        IToken? Resolve(IToken? value) => value is not IndirectReferenceToken reference ? value : scenario == "io" ? throw new IOException("opaque") : reference.Data == actionReference.Data ? multiple : annotation;
        var reader = new PdfPigPackageHyperlinkReader(Resolve, new() { MaxPdfObjects = scenario == "budget" ? 1 : 500 }, CancellationToken.None);
        _ = reader.Read([page], new Dictionary<IndirectReference, int> { [pageReference] = 1 }, scenario == "unknown-pages" ? null : 1);
        if (expected is null) Assert.Empty(reader.MultipleActions);
        else Assert.Equal(expected, Assert.Single(reader.MultipleActions).Count);
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
    [InlineData("single-count")]
    [InlineData("excess-count")]
    [InlineData("label-only")]
    public async Task LegacyOrContradictoryFactsCannotProveMultipleActions(string scenario)
    {
        var fixture = await Fixture(Build("dictionary"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("page[1]/A/Next", 1, new(FileSpecification: "target.pdf"))], [], false, false,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            qualifiedPageCount: scenario == "unqualified-pages" ? null : scenario == "page-conflict" ? 2 : 1,
            multipleHyperlinkActions: scenario == "label-only" ? [] : [new("page[1]/annotation[1]/A", scenario == "wrong-page" ? 2 : 1, scenario == "single-count" ? 1 : scenario == "excess-count" ? int.MaxValue : 2)]);
        var run = new FdaPdfMultipleHyperlinkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, false)]
    [InlineData(1, CheckStatus.Fail, false)]
    [InlineData(0, CheckStatus.NotEvaluated, true)]
    [InlineData(1, CheckStatus.Fail, true)]
    public async Task FindingBudgetAndRepeatedObservationAreBounded(int remaining, CheckStatus expected, bool xyz)
    {
        var fixture = await Fixture(Build("dictionary", xyz ? "[0 /XYZ null null null]" : "[0 /Fit]")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfMultipleHyperlinkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure, bool xyz)
    {
        var fixture = await Fixture(Build("dictionary", xyz ? "[0 /XYZ null null null]" : "[0 /Fit]")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfMultipleHyperlinkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
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
        var result = await Run(await Fixture(Build("dictionary"), limits: new() { MaxXmlFindings = limit }));
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
        var result = await Run(await Fixture(Build("dictionary"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialCoverageRetainsLocatedFailure(bool xyz)
    {
        var fixture = await Fixture(Build("dictionary", xyz ? "[0 /XYZ null null null]" : "[0 /Fit]"));
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
        public string SourceId => "fda5203";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string scenario, string destination = "[0 /Fit]", string nextDestination = "[0 /Fit]")
    {
        var simple = "<< /S /GoToR /F (target.pdf) /D " + nextDestination + " >>";
        var next = scenario switch {
            "single" => "", "array" => " /Next [" + simple + "]", "three" => " /Next [" + simple + " " + simple + "]",
            "nested" => " /Next << /S /GoToR /F (target.pdf) /D " + nextDestination + " /Next " + simple + " >>",
            "empty-array" => " /Next []", "null" => " /Next null", "invalid" => " /Next 12", "indirect" or "cycle" => " /Next 7 0 R",
            "unknown-action" => " /Next << /S /Unknown >>", "wrong-type" => " /Next << /Type /Bogus /S /GoToR /F (target.pdf) /D [0 /Fit] >>",
            "bad-file" => " /Next << /S /GoToR /D [0 /Fit] >>", _ => " /Next " + simple };
        var action = "<< /S /GoToR /F (target.pdf) /D " + destination + next + " >>";
        if (scenario == "bookmark") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (bookmark) /Parent 7 0 R /A " + action + " >>");
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/A " + action + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "") + (scenario == "hidden" ? " /F 2" : ""), "",
            scenario == "cycle" ? "<< /S /GoToR /F (target.pdf) /D " + nextDestination + " /Next 7 0 R >>" : simple);
    }
}
