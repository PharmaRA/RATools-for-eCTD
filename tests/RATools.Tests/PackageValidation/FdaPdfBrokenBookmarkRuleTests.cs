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
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfBrokenBookmarkRuleTests
{
    private const string RuleId = "FDA47-5100";

    [Theory]
    [InlineData("[0 /XYZ null null null]")]
    [InlineData("[0 /XYZ 0 100 0]")]
    [InlineData("[0 /XYZ null null 1.25]")]
    public async Task QualifiedRemoteXyzBookmarkRetainsMissingFileIdentity(string destination)
    {
        var result = await Run(await Fixture(Build("missing.pdf", destinationOverride: destination)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.True(facts.Basics.HasBookmarks);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("0001/missing.pdf", Assert.Single(check.Findings).Actual);
        Assert.Equal(0L, Assert.Single(facts.BookmarkFiles).ZeroBasedPageIndex);
    }

    [Theory]
    [InlineData("[0 /XYZ null null -1]")]
    [InlineData("[0 /XYZ null null (1)]")]
    [InlineData("[0 /XYZ (0) null 1]")]
    [InlineData("[0 /XYZ null false 1]")]
    [InlineData("[0 /XYZ null null]")]
    [InlineData("[0 /XYZ null null 1 0]")]
    [InlineData("[-1 /XYZ null null 1]")]
    [InlineData("[0.5 /XYZ null null 1]")]
    [InlineData("[3 0 R /XYZ null null 1]")]
    [InlineData("[0 /Unknown null null 1]")]
    public async Task InvalidRemoteXyzParametersCannotAuthorizeFileFailure(string destination)
    {
        var result = await Run(await Fixture(Build("missing.pdf", destinationOverride: destination)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Empty(facts.BookmarkFiles);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("../0000/missing.pdf", false, null, CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    [InlineData("../0000/missing.pdf", true, null, CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("target.pdf", false, "target.pdf", CheckStatus.NotEvaluated, CheckStatus.Fail)]
    [InlineData("../0000/target.pdf", true, "target.pdf", CheckStatus.NotEvaluated, CheckStatus.Fail)]
    [InlineData("target%2Epdf", false, "target.pdf", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("target%2Epdf", false, "target%2Epdf", CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    [InlineData("target.pdf#page=1", false, "target.pdf", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("../0002/missing.pdf", false, null, CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    public async Task XyzFileIdentityPreservesLiteralInventoryAndCorruptionSeparation(string target, bool history, string? existing,
        CheckStatus missing, CheckStatus corrupt)
    {
        var result = await Run(await Fixture(Build(target, destinationOverride: "[0 /XYZ null null null]"), history, existing));
        var source = Assert.Single(result.Documents, item => item.LogicalPath == "0001/report.pdf");
        Assert.Null(source.Facts!.Basics.ParseError);
        Assert.Single(source.Facts.BookmarkFiles);
        Assert.Equal(missing, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(corrupt, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5101").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5102").Status);
    }

    [Theory]
    [InlineData("bookmark", CheckStatus.Fail)]
    [InlineData("file-dictionary", CheckStatus.Fail)]
    [InlineData("same-uf", CheckStatus.Fail)]
    [InlineData("conflict-uf", CheckStatus.NotEvaluated)]
    [InlineData("embedded", CheckStatus.NotEvaluated)]
    [InlineData("url-filespec", CheckStatus.NotEvaluated)]
    [InlineData("conflict-dest", CheckStatus.NotEvaluated)]
    [InlineData("next-action", CheckStatus.NotEvaluated)]
    [InlineData("bad-parent", CheckStatus.NotEvaluated)]
    [InlineData("wrong-action-type", CheckStatus.NotEvaluated)]
    [InlineData("page-link", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    public async Task XyzFileTargetsKeepIndependentBookmarkQualification(string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("/submission/missing.pdf", scenario, "[0 /XYZ null null null]")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5115").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task XyzMissingFileHonorsAllowanceAndRetainsCancelledProgress(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("missing.pdf", destinationOverride: "[0 /XYZ null null null]"));
        var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfBrokenBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); Assert.Equal(expected, run.Finish(true)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfBrokenBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(document, 1));
        Assert.Equal(CheckStatus.Fail, retained!.Status); Assert.Single(retained.Findings);
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
    [InlineData("https://example.invalid/missing.pdf", false, null, CheckStatus.NotEvaluated)]
    public async Task MissingFileRequiresExplicitTargetInventory(string target, bool history, string? existing, CheckStatus expected)
    {
        var fixture = await Fixture(Build(target), history, existing);
        var result = await Run(fixture);
        var source = Assert.Single(result.Documents, item => item.LogicalPath == "0001/report.pdf");
        Assert.Null(source.Facts!.Basics.ParseError);
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
    [InlineData("bookmark", CheckStatus.Fail)]
    [InlineData("page-link", CheckStatus.NotEvaluated)]
    [InlineData("open-action", CheckStatus.NotEvaluated)]
    [InlineData("uri", CheckStatus.NotEvaluated)]
    [InlineData("internal", CheckStatus.NotEvaluated)]
    [InlineData("bad-parent", CheckStatus.NotEvaluated)]
    [InlineData("no-title", CheckStatus.NotEvaluated)]
    [InlineData("conflict-dest", CheckStatus.NotEvaluated)]
    [InlineData("next-action", CheckStatus.NotEvaluated)]
    [InlineData("file-dictionary", CheckStatus.Fail)]
    [InlineData("conflict-uf", CheckStatus.NotEvaluated)]
    [InlineData("bad-last", CheckStatus.NotEvaluated)]
    [InlineData("no-destination", CheckStatus.NotEvaluated)]
    public async Task OnlyQualifiedBookmarkFileTargetsAreRegulatoryEvidence(string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("missing.pdf", scenario)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsNotAssumed(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("missing.pdf"), date: date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("contradictory-basics")]
    [InlineData("wrong-action-type")]
    public async Task ReviewRejectsContradictoryBookmarkIdentity(string scenario)
    {
        var fixture = await Fixture(Build("missing.pdf", scenario));
        if (scenario == "wrong-action-type")
        {
            var result = await Run(fixture);
            Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        }
        else
        {
            var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
            var basics = new PdfInspectionResult("1.4", false, false, false, true, [], false, [], PageCount: 1);
            var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
                bookmarkFiles: [new("outline[1]/A/F", "missing.pdf")]);
            var run = new FdaPdfBrokenBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
            run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
            Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        }
    }

    [Theory]
    [InlineData("siblings", 2)]
    [InlineData("nested", 2)]
    [InlineData("bad-prev", 0)]
    [InlineData("bad-child-parent", 0)]
    [InlineData("same-uf", 1)]
    [InlineData("embedded", 0)]
    [InlineData("url-filespec", 0)]
    [InlineData("named", 1)]
    [InlineData("name", 1)]
    [InlineData("bad-destination", 0)]
    [InlineData("hex-file", 1)]
    public void AdapterRequiresConsistentTreeAndFileSemantics(string scenario, int count)
    {
        using var stream = new MemoryStream(Build("missing.pdf", scenario));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(count, facts.BookmarkFiles.Count);
        Assert.All(facts.BookmarkFiles, item => Assert.Equal("missing.pdf", item.FileSpecification));
    }

    [Fact]
    public async Task CyclicNavigationLimitCannotProveMissingBookmarkFile()
    {
        var result = await Run(await Fixture(Build("missing.pdf", "cycle"), limits: new() { MaxPdfLinks = 8 }));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Theory]
    [InlineData("cycle", 200, 0)]
    [InlineData("bad-child-parent", 200, 0)]
    [InlineData("siblings", 200, 2)]
    [InlineData("siblings", 1, 0)]
    public void SupplementaryReaderRejectsCyclesAndIncompleteBoundedTrees(string scenario, int budget, int count)
    {
        using var stream = new MemoryStream(Build("missing.pdf", scenario));
        using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = false });
        IToken? Resolve(IToken? value) => value is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data)?.Data : value;
        var reader = new PdfPigPackageBookmarkReader(Resolve, new() { MaxPdfObjects = budget }, CancellationToken.None);
        Assert.Equal(count, reader.Read(document.Structure.Catalog.CatalogDictionary).Count);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("missing-facts")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    [InlineData("label-only")]
    [InlineData("foreign-path")]
    public async Task UnboundOrLegacyFactsCannotProveMissingBookmarkFile(string scenario)
    {
        var fixture = await Fixture(Build("missing.pdf"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, false, true, [], true, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("outline[1]/GoToR", null, new(FileSpecification: "missing.pdf"))], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            bookmarkFiles: scenario == "label-only" ? [] : [new("outline[1]/A/F", "missing.pdf")]);
        var run = new FdaPdfBrokenBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(scenario == "foreign-path" ? "0000/report.pdf" : file.LogicalPath, scenario == "missing-facts" ? null : facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("missing.pdf", "siblings"));
        var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfBrokenBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, remaining);
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
            var run = new FdaPdfBrokenBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task SharedBudgetPreservesKnownBookmarkFailure(int limit)
    {
        var result = await Run(await Fixture(Build("missing.pdf"), limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Theory]
    [InlineData(false, CheckStatus.NotApplicable)]
    [InlineData(true, CheckStatus.NotEvaluated)]
    public async Task NoPdfAndUnreadableSourceAreNotMissingTargetEvidence(bool broken, CheckStatus expected)
    {
        var result = await Run(await Fixture(broken ? "broken"u8.ToArray() : null));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task MissingArchivedSourceStaysUnknown()
    {
        var result = await Run(await Fixture(Build("missing.pdf"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialCoverageDoesNotPromiseAllBookmarkActions()
    {
        var fixture = await Fixture(Build("missing.pdf"));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, bool history = false, string? existing = null, string? date = "2026-11-25", PackageReadLimits? limits = null, bool missingSource = false)
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
            // Deliberately unreadable: existence, not successful opening, is the 5100 question.
            if (existing is not null) _files[sequence + "/" + existing] = "not a PDF"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5100-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string target, string scenario = "bookmark", string? destinationOverride = null)
    {
        var file = scenario switch {
            "file-dictionary" => "<< /Type /Filespec /F (" + target + ") >>",
            "conflict-uf" => "<< /F (" + target + ") /UF (other.pdf) >>",
            "same-uf" => "<< /F (" + target + ") /UF (" + target + ") >>",
            "embedded" => "<< /F (" + target + ") /EF << /F 6 0 R >> >>",
            "url-filespec" => "<< /FS /URL /F (" + target + ") >>",
            "hex-file" => "<" + Convert.ToHexString(Encoding.ASCII.GetBytes(target)) + ">", _ => "(" + target + ")" };
        var destination = scenario switch { "no-destination" => "", "named" => "/D (target)", "name" => "/D /target", "bad-destination" => "/D [-1 /Fit]", _ => "/D [0 /Fit]" };
        if (destinationOverride is not null) destination = "/D " + destinationOverride;
        var action = scenario switch { "uri" => "<< /S /URI /URI (" + target + ") >>", "internal" => "<< /S /GoTo /D [3 0 R /Fit] >>", _ => "<< /S /GoToR /F " + file + " " + destination + (scenario == "wrong-action-type" ? " /Type /Bogus" : "") + (scenario == "next-action" ? " /Next << /S /GoToR /F (other.pdf) /D [0 /Fit] >>" : "") + " >>" };
        var siblings = scenario is "siblings" or "bad-prev";
        var nested = scenario is "nested" or "bad-child-parent";
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R " + (scenario == "page-link" ? "" : scenario == "open-action" ? "/OpenAction " + action : "/Outlines 4 0 R") + " >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " + (scenario == "page-link" ? "/Annots [6 0 R]" : "") + " >>",
            "<< /Type /Outlines /First 5 0 R /Last " + (scenario == "bad-last" || siblings ? "6" : "5") + " 0 R /Count " + (siblings || nested ? "2" : "1") + " >>",
            "<< " + (scenario == "no-title" ? "" : "/Title (bookmark)") + " /Parent " + (scenario == "bad-parent" ? "1" : "4") + " 0 R /A " + action + (scenario == "conflict-dest" ? " /Dest [3 0 R /Fit]" : "") + (siblings ? " /Next 6 0 R" : nested ? " /First 6 0 R /Last 6 0 R /Count 1" : scenario == "cycle" ? " /Next 5 0 R" : "") + " >>",
            siblings || nested ? "<< /Title (second) /Parent " + (scenario == "nested" ? "5" : "4") + " 0 R " + (siblings ? "/Prev " + (scenario == "bad-prev" ? "4" : "5") + " 0 R " : "") + "/A " + action + " >>" : "<< /Type /Annot /Subtype /Link /Rect [0 0 100 100] /A " + action + " >>"
        };
        var output = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++) { offsets.Add(Encoding.ASCII.GetByteCount(output.ToString())); output.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(output.ToString()); output.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) output.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        output.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(output.ToString());
    }
}
