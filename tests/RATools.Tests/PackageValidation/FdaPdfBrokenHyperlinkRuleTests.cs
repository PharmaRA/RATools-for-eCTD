using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

public sealed class FdaPdfBrokenHyperlinkRuleTests
{
    private const string RuleId = "FDA47-5200";

    [Theory]
    [InlineData("[0 /XYZ null null null]")]
    [InlineData("[0 /XYZ 0 100 0]")]
    [InlineData("[0 /XYZ null null 1.25]")]
    public async Task QualifiedXyzPageLinkRetainsMissingFileIdentity(string destination)
    {
        var result = await Run(await Fixture(Build("missing.pdf", destination: destination)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("0001/missing.pdf", Assert.Single(check.Findings).Actual);
        Assert.Equal(0, Assert.Single(facts.HyperlinkFiles).ZeroBasedPageIndex);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5202").Status);
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
    [InlineData("[0 /Fit null null null]")]
    public async Task InvalidXyzDestinationDoesNotPublishFileIdentity(string destination)
    {
        var result = await Run(await Fixture(Build("missing.pdf", destination: destination)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Empty(facts.HyperlinkFiles);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("../0000/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    [InlineData("../0000/missing.pdf", true, null, CheckStatus.Fail)]
    [InlineData("target.pdf", false, "target.pdf", CheckStatus.NotEvaluated)]
    [InlineData("../0000/target.pdf", true, "target.pdf", CheckStatus.NotEvaluated)]
    [InlineData("target%2Epdf", false, "target.pdf", CheckStatus.Fail)]
    [InlineData("target%2Epdf", false, "target%2Epdf", CheckStatus.NotEvaluated)]
    [InlineData("target.pdf#page=1", false, "target.pdf", CheckStatus.Fail)]
    [InlineData("../0002/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    [InlineData("../../outside.pdf", false, null, CheckStatus.NotEvaluated)]
    [InlineData("/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    public async Task XyzFileIdentityRetainsSelectedInventoryAndLiteralPathBoundaries(string target, bool history, string? existing, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(target, destination: "[0 /XYZ null null null]"), history, existing));
        var facts = Assert.Single(result.Documents, item => item.LogicalPath == "0001/report.pdf").Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Equal(0, Assert.Single(facts.HyperlinkFiles).ZeroBasedPageIndex);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
        Assert.Equal(target is "target.pdf" or "../0000/target.pdf" ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            Assert.Single(result.Checks, item => item.RuleId == "FDA47-5201").Status);
        Assert.Equal(target == "/missing.pdf" ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            Assert.Single(result.Checks, item => item.RuleId == "FDA47-5215").Status);
    }

    [Theory]
    [InlineData("link", CheckStatus.Fail)]
    [InlineData("owner", CheckStatus.Fail)]
    [InlineData("filespec", CheckStatus.Fail)]
    [InlineData("bookmark", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    [InlineData("uri", CheckStatus.NotEvaluated)]
    [InlineData("text-annotation", CheckStatus.NotEvaluated)]
    [InlineData("wrong-owner", CheckStatus.NotEvaluated)]
    [InlineData("conflict", CheckStatus.NotEvaluated)]
    [InlineData("next", CheckStatus.NotEvaluated)]
    [InlineData("conflict-uf", CheckStatus.NotEvaluated)]
    [InlineData("unknown-filesystem", CheckStatus.NotEvaluated)]
    [InlineData("hidden", CheckStatus.NotEvaluated)]
    [InlineData("zero-rect", CheckStatus.NotEvaluated)]
    public async Task XyzDestinationDoesNotBypassPageLinkQualification(string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("missing.pdf", scenario, "[0 /XYZ null null null]")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("missing.pdf", false, null, CheckStatus.Fail)]
    [InlineData("../0000/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    [InlineData("../0000/missing.pdf", true, null, CheckStatus.Fail)]
    [InlineData("target.pdf", false, "target.pdf", CheckStatus.NotEvaluated)]
    [InlineData("../0000/target.pdf", true, "target.pdf", CheckStatus.NotEvaluated)]
    [InlineData("target%2Epdf", false, "target.pdf", CheckStatus.Fail)]
    [InlineData("target%2Epdf", false, "target%2Epdf", CheckStatus.NotEvaluated)]
    [InlineData("target.pdf#page=1", false, "target.pdf", CheckStatus.Fail)]
    [InlineData("../0002/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    [InlineData("../../outside.pdf", false, null, CheckStatus.NotEvaluated)]
    [InlineData("/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    public async Task MissingTargetRequiresExplicitCapturedInventory(string target, bool history, string? existing, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(target), history, existing));
        Assert.Null(Assert.Single(result.Documents, item => item.LogicalPath == "0001/report.pdf").Facts!.Basics.ParseError);
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
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5100").Status);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("link", CheckStatus.Fail)]
    [InlineData("owner", CheckStatus.Fail)]
    [InlineData("filespec", CheckStatus.Fail)]
    [InlineData("bookmark", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    [InlineData("uri", CheckStatus.NotEvaluated)]
    [InlineData("text-annotation", CheckStatus.NotEvaluated)]
    [InlineData("wrong-owner", CheckStatus.NotEvaluated)]
    [InlineData("conflict", CheckStatus.NotEvaluated)]
    [InlineData("next", CheckStatus.NotEvaluated)]
    [InlineData("conflict-uf", CheckStatus.NotEvaluated)]
    [InlineData("unknown-filesystem", CheckStatus.NotEvaluated)]
    [InlineData("hidden", CheckStatus.NotEvaluated)]
    [InlineData("zero-rect", CheckStatus.NotEvaluated)]
    [InlineData("wrong-page-parent", CheckStatus.NotEvaluated)]
    public async Task RegulatoryLinkIdentityDoesNotComeFromGenericObjectPath(string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("missing.pdf", scenario)));
        var error = Assert.Single(result.Documents).Facts!.Basics.ParseError;
        if (scenario == "wrong-page-parent") Assert.NotNull(error);
        else Assert.Null(error);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("missing.pdf"), date: date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("valid", 1)]
    [InlineData("duplicate", 0)]
    [InlineData("no-page-qualification", 0)]
    [InlineData("bad-rect", 0)]
    [InlineData("zero-rect", 0)]
    [InlineData("bad-type", 0)]
    [InlineData("wrong-owner", 0)]
    [InlineData("flags", 0)]
    [InlineData("quadpoints", 0)]
    [InlineData("budget", 0)]
    [InlineData("io", 0)]
    public void IndependentHyperlinkIdentityRequiresConsistentBoundedEvidence(string scenario, int expected)
    {
        var pageRef = new IndirectReference(3, 0); var annotationRef = new IndirectReferenceToken(new(6, 0));
        static DictionaryToken Dictionary(params (string Key, IToken Value)[] pairs) => new(pairs.ToDictionary(pair => NameToken.Create(pair.Key), pair => pair.Value));
        var action = Dictionary(("S", NameToken.Create("GoToR")), ("F", new StringToken("missing.pdf")),
            ("D", new ArrayToken([new NumericToken(0), NameToken.Create("Fit")])));
        var values = new Dictionary<NameToken, IToken> {
            [NameToken.Create("Type")] = NameToken.Create(scenario == "bad-type" ? "Bogus" : "Annot"),
            [NameToken.Create("Subtype")] = NameToken.Create("Link"), [NameToken.Create("A")] = action,
            [NameToken.Create("Rect")] = scenario == "bad-rect" ? new ArrayToken([new NumericToken(0)]) :
                new ArrayToken([new NumericToken(0), new NumericToken(0), new NumericToken(scenario == "zero-rect" ? 0 : 10), new NumericToken(10)]) };
        if (scenario == "wrong-owner") values[NameToken.Create("P")] = new IndirectReferenceToken(new(9, 0));
        if (scenario == "flags") values[NameToken.Create("F")] = new NumericToken(2);
        if (scenario == "quadpoints") values[NameToken.Create("QuadPoints")] = new ArrayToken([]);
        var annotation = new DictionaryToken(values);
        var page = Dictionary(("Annots", new ArrayToken(scenario == "duplicate" ? [annotationRef, annotationRef] : [annotationRef])));
        IToken? Resolve(IToken? value) => value is not IndirectReferenceToken ? value : scenario == "io" ? throw new IOException("opaque") : annotation;
        var reader = new PdfPigPackageHyperlinkReader(Resolve, new() { MaxPdfObjects = scenario == "budget" ? 1 : 200 }, CancellationToken.None);
        var result = reader.Read([page], new Dictionary<IndirectReference, int> { [pageRef] = 1 }, scenario == "no-page-qualification" ? null : 1);
        Assert.Equal(expected, result.Count);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("unqualified-pages")]
    [InlineData("page-count-conflict")]
    [InlineData("bad-source-page")]
    [InlineData("label-only")]
    [InlineData("historical-source")]
    public async Task UnboundOrContradictoryFactsCannotProveMissingHyperlinkTarget(string scenario)
    {
        var fixture = await Fixture(Build("missing.pdf"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("page[1]/annotation[1]/GoToR", 1, new(FileSpecification: "missing.pdf"))], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            qualifiedPageCount: scenario == "unqualified-pages" ? null : scenario == "page-count-conflict" ? 2 : 1,
            hyperlinkFiles: scenario == "label-only" ? [] : [new("page[1]/annotation[1]/A/F", scenario == "bad-source-page" ? 2 : 1, "missing.pdf")]);
        var run = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(scenario == "historical-source" ? "0000/report.pdf" : file.LogicalPath, facts,
            scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("missing.pdf")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("missing.pdf")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
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
        var result = await Run(await Fixture(Build("missing.pdf"), limits: new() { MaxXmlFindings = limit }));
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
        var result = await Run(await Fixture(Build("missing.pdf"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialReportRetainsLocatedFailure()
    {
        var fixture = await Fixture(Build("missing.pdf"));
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
    public async Task ConflictingLinkIdentityCannotAuthorizeMissingTarget(bool pageConflict, bool independent)
    {
        var fixture = await Fixture(Build("missing.pdf"), existing: "present.pdf");
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var original = new PdfHyperlinkFileTarget("page[1]/annotation[1]/A/F", 1, "missing.pdf");
        var conflicting = pageConflict ? original with { PageNumber = 2 } : original with { FileSpecification = "present.pdf" };
        var links = new List<PdfHyperlinkFileTarget> { original, conflicting };
        if (independent) links.Add(new("page[1]/annotation[2]/A/F", 1, "other-missing.pdf"));
        var basics = new PdfInspectionResult("1.4", false, false, true, true, [], false, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
            qualifiedPageCount: 1, hyperlinkFiles: links);
        var run = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(independent ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(independent ? 1 : 0, check.Findings.Count);
        if (independent) Assert.Equal("page[1]/annotation[2]/A/F", Assert.Single(check.Findings).Location.FieldPath);
        links.Reverse();
        foreach (var remaining in new[] { 0, 1 })
        {
            var reversed = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
                qualifiedPageCount: 1, hyperlinkFiles: links);
            var bounded = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
            bounded.Observe(new(file.LogicalPath, reversed, file.Sha256), remaining);
            Assert.Equal(independent ? remaining : 0, bounded.FindingCount);
        }
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("page[1]/annotation[1]/A/F", 0)]
    [InlineData("page[1]/annotation[2]/A/F", 2)]
    public async Task DuplicateOrUnlocatedTargetsDoNotAuthorizeExtraFindings(string? otherPath, int expected)
    {
        var fixture = await Fixture(Build("missing.pdf"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", false, false, true, true, [], false, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
            qualifiedPageCount: 1, hyperlinkFiles: [new(otherPath!, 1, "missing.pdf"), new("page[1]/annotation[1]/A/F", 1, "missing.pdf")]);
        var run = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        Assert.Equal(expected, run.FindingCount);
        Assert.Equal(expected > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, run.Finish(false)!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteLinkUniquenessScanCannotPublish(bool conflictLast)
    {
        var baseline = await Fixture(Build("missing.pdf"));
        var fixture = await Fixture(Build("missing.pdf"), limits: new PackageReadLimits { MaxPdfObjects = baseline.Input.Packages.Target.Manifest.Files.Count + 3 });
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", false, false, true, true, [], false, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
            qualifiedPageCount: 1, hyperlinkFiles: [new("page[1]/annotation[1]/A/F", 1, "missing.pdf"), new("page[1]/annotation[2]/A/F", 1, "other.pdf"),
                new(conflictLast ? "page[1]/annotation[1]/A/F" : "page[1]/annotation[3]/A/F", 2, "missing.pdf")]);
        var run = new FdaPdfBrokenHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, bool history = false, string? existing = null, string? date = "2026-11-26", PackageReadLimits? limits = null, bool missingSource = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, item => item.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", pdf, history ? null : existing, limits ?? new());
        var old = history ? new Capture(app, "0000", null, existing, target.Limits) : null;
        var baseline = new HistoryBaselineManifest(app, "0001", old is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, old.SourceId, old.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
        var packages = new PackageInputSet(target, baseline, old is null ? [] : [old]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, byte[]? pdf, string? existing, PackageReadLimits limits)
        {
            Limits = limits;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (pdf is not null) _files[sequence + "/report.pdf"] = pdf;
            if (existing is not null) _files[sequence + "/" + existing] = "not a PDF"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5200-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string target, string scenario = "link", string destination = "[0 /Fit]")
    {
        var file = scenario switch { "filespec" => "<< /Type /Filespec /F (" + target + ") >>", "conflict-uf" => "<< /F (" + target + ") /UF (other.pdf) >>", "unknown-filesystem" => "<< /FS /URL /F (" + target + ") >>", _ => "(" + target + ")" };
        var action = scenario == "uri" ? "<< /S /URI /URI (" + target + ") >>" : "<< /S /GoToR /F " + file + " /D " + destination + (scenario == "next" ? " /Next << /S /GoToR /F (other.pdf) /D [0 /Fit] >>" : "") + " >>";
        if (scenario == "bookmark") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (bookmark) /Parent 7 0 R /A " + action + " >>");
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        var properties = "/A " + action + (scenario == "owner" ? " /P 3 0 R" : scenario == "wrong-owner" ? " /P 2 0 R" : "") +
            (scenario == "hidden" ? " /F 2" : "") + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "");
        var text = Encoding.ASCII.GetString(PdfPigPackageInspectorTests.Build(properties));
        // Equal-length replacements preserve xref offsets.
        if (scenario == "text-annotation") text = text.Replace("/Subtype /Link", "/Subtype /Text", StringComparison.Ordinal);
        if (scenario == "zero-rect") text = text.Replace("/Rect [0 0 100 100]", "/Rect [0 0 000 100]", StringComparison.Ordinal);
        if (scenario == "wrong-page-parent") text = text.Replace("/Parent 2 0 R", "/Parent 1 0 R", StringComparison.Ordinal);
        return Encoding.ASCII.GetBytes(text);
    }
}
