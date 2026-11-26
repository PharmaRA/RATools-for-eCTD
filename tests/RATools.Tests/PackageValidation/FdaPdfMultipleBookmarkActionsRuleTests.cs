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

public sealed class FdaPdfMultipleBookmarkActionsRuleTests
{
    private const string RuleId = "FDA47-5103";

    [Theory]
    [InlineData("dictionary", "[0 /XYZ null null null]", "[0 /Fit]", 2)]
    [InlineData("dictionary", "[0 /Fit]", "[0 /XYZ null null 0]", 2)]
    [InlineData("three", "[0 /XYZ null null 1]", "[1 /XYZ 0 100 1.25]", 3)]
    [InlineData("indirect", "[0 /XYZ null null null]", "[0 /XYZ null null null]", 2)]
    public async Task CompleteXyzChainsRetainAllBookmarkActionExecutions(string scenario, string first, string following, int count)
    {
        var result = await Run(await Fixture(Build(scenario, first, following)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.True(facts.Basics.HasBookmarks);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(count.ToString(CultureInfo.InvariantCulture), Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData("[0 /XYZ null null -1]")]
    [InlineData("[0 /XYZ null null (1)]")]
    [InlineData("[0 /XYZ false null 1]")]
    [InlineData("[0 /XYZ null null]")]
    [InlineData("[0 /XYZ null null 1 0]")]
    [InlineData("[-1 /XYZ null null null]")]
    [InlineData("[0.5 /XYZ null null null]")]
    [InlineData("[3 0 R /XYZ null null null]")]
    public async Task InvalidXyzTailDoesNotPublishPartialActionCount(string following)
    {
        var result = await Run(await Fixture(Build("dictionary", "[0 /XYZ null null null]", following)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Empty(facts.MultipleBookmarkActions);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("siblings")]
    [InlineData("empty-array")]
    [InlineData("null")]
    [InlineData("invalid")]
    [InlineData("unknown-action")]
    [InlineData("cycle")]
    [InlineData("conflict")]
    [InlineData("bad-parent")]
    [InlineData("no-title")]
    [InlineData("page-link")]
    [InlineData("open-action")]
    public async Task XyzChainKeepsBookmarkIdentityAndCompleteTailRequirements(string scenario)
    {
        var result = await Run(await Fixture(Build(scenario, "[0 /XYZ null null null]", "[0 /XYZ null null null]")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Empty(facts.MultipleBookmarkActions);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        if (scenario == "page-link")
        {
            Assert.Empty(facts.MultipleHyperlinkActions);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5203").Status);
        }
    }

    [Theory]
    [InlineData(false, 200, 64, 100, null)]
    [InlineData(true, 200, 64, 100, 3)]
    [InlineData(true, 1, 64, 100, null)]
    [InlineData(true, 200, 0, 100, null)]
    [InlineData(true, 200, 64, 2, null)]
    public void XyzActionReaderRequiresExplicitEnablementAndFullBudget(bool enabled, int work, int depth, int links, int? expected)
    {
        using var stream = new MemoryStream(Build("three", "[0 /XYZ null null null]", "[0 /XYZ null null null]"));
        using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false });
        IToken? Resolve(IToken? value) => value is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data)?.Data : value;
        var item = Assert.IsType<DictionaryToken>(document.Structure.GetObject(new(8, 0))!.Data);
        var reader = new PdfPigPackageBookmarkActionReader(Resolve, new() { MaxPdfObjects = work, MaxPdfDepth = depth, MaxPdfLinks = links }, CancellationToken.None, enabled);
        Assert.Equal(expected, reader.Read(item));
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task XyzActionChainHonorsFindingAllowanceAndCancellation(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("dictionary", "[0 /XYZ null null null]", "[0 /XYZ null null null]"));
        var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfMultipleBookmarkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); Assert.Equal(expected, run.Finish(true)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfMultipleBookmarkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(document, 1));
        Assert.Equal(CheckStatus.Fail, retained!.Status); Assert.Single(retained.Findings);
    }

    [Fact]
    public async Task XyzActionChainReportRemainsPartial()
    {
        var fixture = await Fixture(Build("dictionary", "[0 /XYZ null null null]", "[0 /XYZ null null null]"));
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("dictionary", CheckStatus.Fail)]
    [InlineData("array", CheckStatus.Fail)]
    [InlineData("three", CheckStatus.Fail)]
    [InlineData("indirect", CheckStatus.Fail)]
    [InlineData("single", CheckStatus.NotEvaluated)]
    [InlineData("empty-array", CheckStatus.NotEvaluated)]
    [InlineData("null", CheckStatus.NotEvaluated)]
    [InlineData("invalid", CheckStatus.NotEvaluated)]
    [InlineData("unknown-action", CheckStatus.NotEvaluated)]
    [InlineData("wrong-type", CheckStatus.NotEvaluated)]
    [InlineData("bad-file", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("conflict", CheckStatus.NotEvaluated)]
    [InlineData("bad-parent", CheckStatus.NotEvaluated)]
    [InlineData("no-title", CheckStatus.NotEvaluated)]
    [InlineData("siblings", CheckStatus.NotEvaluated)]
    [InlineData("page-link", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    public async Task MultipleActionsRequireQualifiedBookmarkAndActionChain(string scenario, CheckStatus expected)
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
            Assert.Equal(scenario == "three" ? "3" : "2", finding.Actual);
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
    [InlineData("dictionary", 200, 64, 2)]
    [InlineData("three", 200, 64, 3)]
    [InlineData("cycle", 200, 64, null)]
    [InlineData("three", 1, 64, null)]
    [InlineData("nested", 200, 1, null)]
    [InlineData("nested", 200, 2, 3)]
    public void ActionReaderRequiresCompleteBoundedChain(string scenario, int budget, int depth, int? expected)
    {
        using var stream = new MemoryStream(Build(scenario));
        using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false });
        IToken? Resolve(IToken? value) => value is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data)?.Data : value;
        var item = Assert.IsType<DictionaryToken>(document.Structure.GetObject(new(8, 0))!.Data);
        var reader = new PdfPigPackageBookmarkActionReader(Resolve, new() { MaxPdfObjects = budget, MaxPdfDepth = depth }, CancellationToken.None);
        Assert.Equal(expected, reader.Read(item));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("no-bookmarks")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("single-count")]
    [InlineData("excess-count")]
    [InlineData("label-only")]
    public async Task LegacyOrContradictoryFactsCannotProveMultipleActions(string scenario)
    {
        var fixture = await Fixture(Build("dictionary"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], scenario != "no-bookmarks", [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("outline[1]/A/Next", null, new(FileSpecification: "target.pdf"))], [], false, false,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            multipleBookmarkActions: scenario == "label-only" ? [] : [new("outline[1]/A", scenario == "single-count" ? 1 : scenario == "excess-count" ? int.MaxValue : 2)]);
        var run = new FdaPdfMultipleBookmarkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingBudgetAndRepeatedObservationAreBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("dictionary")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfMultipleBookmarkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("dictionary")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfMultipleBookmarkActionsRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
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

    [Fact]
    public async Task PartialCoverageRetainsLocatedFailure()
    {
        var fixture = await Fixture(Build("dictionary"));
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
        public string SourceId => "fda5103";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string scenario, string first = "[0 /Fit]", string following = "[0 /Fit]")
    {
        var simple = "<< /S /GoToR /F (target.pdf) /D " + following + " >>";
        var next = scenario switch {
            "single" or "siblings" => "", "array" => " /Next [" + simple + "]", "three" => " /Next [" + simple + " " + simple + "]",
            "nested" => " /Next << /S /GoToR /F (target.pdf) /D [0 /Fit] /Next " + simple + " >>",
            "empty-array" => " /Next []", "null" => " /Next null", "invalid" => " /Next 12", "indirect" => " /Next 9 0 R",
            "cycle" => " /Next 9 0 R", "unknown-action" => " /Next << /S /Unknown >>", "wrong-type" => " /Next << /Type /Bogus /S /GoToR /F (target.pdf) /D [0 /Fit] >>",
            "bad-file" => " /Next << /S /GoToR /D [0 /Fit] >>", _ => " /Next " + simple };
        var action = "<< /S /GoToR /F (target.pdf) /D " + first + next + " >>";
        if (scenario == "page-link") return PdfPigPackageInspectorTests.Build("/A " + action);
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last " + (scenario == "siblings" ? "9" : "8") + " 0 R /Count " + (scenario == "siblings" ? "2" : "1") + " >>",
            "<< " + (scenario == "no-title" ? "" : "/Title (bookmark)") + " /Parent " + (scenario == "bad-parent" ? "1" : "7") + " 0 R /A " + action + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "") + (scenario == "siblings" ? " /Next 9 0 R" : "") + " >>",
            scenario == "siblings" ? "<< /Title (second) /Parent 7 0 R /Prev 8 0 R /A " + simple + " >>" : scenario == "cycle" ? "<< /S /GoToR /F (target.pdf) /D [0 /Fit] /Next 9 0 R >>" : simple);
    }
}
