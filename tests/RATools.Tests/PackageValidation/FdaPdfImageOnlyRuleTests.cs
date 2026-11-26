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

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfImageOnlyRuleTests
{
    private const string RuleId = "FDA47-5057";

    [Theory]
    [InlineData("single")]
    [InlineData("multiple")]
    [InlineData("state")]
    [InlineData("operands")]
    public async Task CompleteContentArraysCanEstablishImageOnly(string scenario)
    {
        string[] parts = scenario switch
        {
            "single" => ["q /Im1 Do Q"],
            "multiple" => ["q /Im1 Do Q\n", "q /Im1 Do Q"],
            "state" => ["q\n", "/Im1 Do\n", "Q"],
            _ => ["q 100 0 0\n", "100 20 20 cm /Im1\n", "Do Q"]
        };
        var result = await Run(await Fixture(Build("image", contentParts: parts)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.False(facts.Basics.HasSearchableText);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("empty-members", CheckStatus.Fail)]
    [InlineData("empty-only", CheckStatus.NotEvaluated)]
    [InlineData("repeated-stream", CheckStatus.Fail)]
    [InlineData("later-text", CheckStatus.NotEvaluated)]
    [InlineData("earlier-text", CheckStatus.NotEvaluated)]
    [InlineData("empty-text", CheckStatus.NotEvaluated)]
    [InlineData("filtered-first", CheckStatus.NotEvaluated)]
    [InlineData("filtered-last", CheckStatus.NotEvaluated)]
    [InlineData("no-line-boundary", CheckStatus.NotEvaluated)]
    [InlineData("comment-boundary", CheckStatus.NotEvaluated)]
    [InlineData("comment-newline", CheckStatus.Fail)]
    [InlineData("trailing-operand", CheckStatus.NotEvaluated)]
    [InlineData("unclosed-state", CheckStatus.NotEvaluated)]
    [InlineData("blank-then-image", CheckStatus.Fail)]
    public async Task EveryContentArrayMemberParticipatesInQualification(string scenario, CheckStatus expected)
    {
        const string image = "q /Im1 Do Q\n";
        const string text = "BT /F1 12 Tf 3 Tr (OCR) Tj ET\n";
        string[] parts = scenario switch
        {
            "empty-members" => ["", image, ""], "empty-only" => ["", ""],
            "later-text" => [image, text], "earlier-text" => [text, image],
            "empty-text" => [image, "BT /F1 12 Tf () Tj ET"],
            "no-line-boundary" => ["q /Im1 Do Q ", image],
            "comment-boundary" => [image + "% unfinished comment", image],
            "comment-newline" => [image + "% complete comment\n", image],
            "trailing-operand" => [image, "12"], "unclosed-state" => ["q\n", "/Im1 Do\n"],
            "blank-then-image" => [" \n", image], _ => [image, image]
        };
        var filtered = scenario == "filtered-first" ? 0 : scenario == "filtered-last" ? 1 : -1;
        var result = await Run(await Fixture(Build("image", contentParts: parts, filteredPart: filtered,
            contentsOverride: scenario == "repeated-stream" ? "[11 0 R 11 0 R]" : null)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected == CheckStatus.Fail ? PdfImageOnlyStatus.QualifiedImageOnly : PdfImageOnlyStatus.Unknown, facts.ImageOnly);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(1048576, PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData(1048577, PdfImageOnlyStatus.Unknown)]
    public void ArrayByteAllowanceIsAggregatedBeforeConcatenation(int length, PdfImageOnlyStatus expected)
    {
        const string image = "q /Im1 Do Q\n";
        var part = image + new string(' ', 524288 - image.Length - 1) + "\n";
        var tail = image + new string(' ', length - part.Length - image.Length);
        using var stream = new MemoryStream(Build("image", contentParts: [part, tail]));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected, facts.ImageOnly);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[4 0 R 99 0 R]")]
    [InlineData("[4 0 R 6 0 R]")]
    [InlineData("[4 0 R null]")]
    [InlineData("[4 0 R [4 0 R]]")]
    public async Task UnavailableOrNonStreamArrayMembersCannotProveImageOnly(string contents)
    {
        var result = await Run(await Fixture(Build("image", contentsOverride: contents)));
        // Base-parser rejection and supplemental uncertainty both remain unknown
        // for this rule; neither permits ignoring an unqualified array member.
        Assert.Equal(PdfImageOnlyStatus.Unknown, Assert.Single(result.Documents).Facts!.ImageOnly);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ArrayImageFailureUsesSharedAllowanceAndRetainsCancelledProgress(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("image", contentParts: ["q\n", "/Im1 Do\n", "Q"]));
        var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfImageOnlyRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); Assert.Equal(expected, run.Finish(true)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfImageOnlyRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(document, 1));
        Assert.Equal(CheckStatus.Fail, retained!.Status); Assert.Single(retained.Findings);
    }

    [Fact]
    public async Task ArrayImageFailureRemainsPartialInReport()
    {
        var fixture = await Fixture(Build("image", contentParts: ["q\n", "/Im1 Do\n", "Q"]));
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("image", CheckStatus.Fail)]
    [InlineData("two-images", CheckStatus.Fail)]
    [InlineData("comment", CheckStatus.Fail)]
    [InlineData("blank", CheckStatus.NotEvaluated)]
    [InlineData("vector", CheckStatus.NotEvaluated)]
    [InlineData("ocr", CheckStatus.NotEvaluated)]
    [InlineData("empty-text", CheckStatus.NotEvaluated)]
    [InlineData("nested", CheckStatus.NotEvaluated)]
    [InlineData("filtered", CheckStatus.NotEvaluated)]
    [InlineData("annotation", CheckStatus.NotEvaluated)]
    [InlineData("form", CheckStatus.NotEvaluated)]
    [InlineData("structure", CheckStatus.NotEvaluated)]
    [InlineData("unknown-op", CheckStatus.NotEvaluated)]
    [InlineData("two-pages", CheckStatus.Fail)]
    [InlineData("image-and-blank", CheckStatus.NotEvaluated)]
    [InlineData("image-and-text", CheckStatus.NotEvaluated)]
    public async Task ImageOnlyRequiresCompletePositiveImageAndNegativeTextEvidence(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(Build(scenario));
        var result = await Run(fixture);
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(scenario is "ocr" or "image-and-text", facts.Basics.HasSearchableText);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Contains("whenever possible", finding.RecommendedAction);
            Assert.DoesNotContain("Replace", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-20", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-21", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task PolicyIsDateQualified(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build("image"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c")]
    [InlineData("113faa128d34ca5c2b99184da2cf4da2e4359403204289892720c1f73afcae2a")]
    public async Task BothArchivedSourcesAreRequired(string hash)
    {
        var result = await Run(await Fixture(Build("image"), missingHash: hash));
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
    [InlineData("q 100 0 0 100 20 20 cm /Im1 Do Q", PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData("q /Im1 Do Q % text-like comment", PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData("q /Im1 Do Q 12", PdfImageOnlyStatus.Unknown)]
    [InlineData("q /Im1 Do", PdfImageOnlyStatus.Unknown)]
    [InlineData("q 0 0 0 0 20 20 cm /Im1 Do Q", PdfImageOnlyStatus.Unknown)]
    [InlineData("q /Im1 Do Q BT /F1 12 Tf () Tj ET", PdfImageOnlyStatus.Unknown)]
    public void CompleteContentGrammarIsRequired(string content, PdfImageOnlyStatus expected)
    {
        using var stream = new MemoryStream(Build("image", content));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.False(facts.Basics.HasSearchableText);
        Assert.Equal(expected, facts.ImageOnly);
    }

    [Fact]
    public async Task BaseParserRejectedGraphicsStateCannotProveImageOnly()
    {
        var result = await Run(await Fixture(Build("image", "/Im1 Do Q")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.NotNull(facts.Basics.ParseError);
        Assert.Equal(PdfImageOnlyStatus.Unknown, facts.ImageOnly);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(1048576, PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData(1048577, PdfImageOnlyStatus.Unknown)]
    public void FullStreamByteAllowanceIsExact(int length, PdfImageOnlyStatus expected)
    {
        const string operations = "q /Im1 Do Q";
        using var stream = new MemoryStream(Build("image", operations + new string(' ', length - operations.Length)));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Equal(expected, facts.ImageOnly);
    }

    [Theory]
    [InlineData(" ", "/DeviceGray", PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData("   ", "/DeviceRGB", PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData("  ", "/DeviceGray", PdfImageOnlyStatus.Unknown)]
    [InlineData("  ", "/DeviceRGB", PdfImageOnlyStatus.Unknown)]
    public void ImageBytesMustAgreeWithDimensionsAndColorSpace(string data, string colorSpace, PdfImageOnlyStatus expected)
    {
        using var stream = new MemoryStream(Build("image", imageData: data, colorSpace: colorSpace));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected, facts.ImageOnly);
    }

    [Theory]
    [InlineData("image", PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData("two-pages", PdfImageOnlyStatus.QualifiedImageOnly)]
    [InlineData("blank", PdfImageOnlyStatus.Unknown)]
    [InlineData("ocr", PdfImageOnlyStatus.Unknown)]
    [InlineData("nested", PdfImageOnlyStatus.Unknown)]
    [InlineData("filtered", PdfImageOnlyStatus.Unknown)]
    [InlineData("annotation", PdfImageOnlyStatus.Unknown)]
    [InlineData("image-and-text", PdfImageOnlyStatus.Unknown)]
    public void AdapterKeepsQualificationSeparateFromTextExtraction(string scenario, PdfImageOnlyStatus expected)
    {
        using var stream = new MemoryStream(Build(scenario));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected, facts.ImageOnly);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("missing-facts")]
    [InlineData("unknown")]
    [InlineData("searchable")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("no-pages")]
    public async Task UnqualifiedOrContradictoryFactsCannotProveImageOnly(string scenario)
    {
        var fixture = await Fixture(Build("image"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, scenario == "searchable", true, [], false, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            imageOnly: scenario == "unknown" ? PdfImageOnlyStatus.Unknown : PdfImageOnlyStatus.QualifiedImageOnly);
        var run = new FdaPdfImageOnlyRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, scenario == "missing-facts" ? null : facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceAndInterruptedFinish(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("image")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfImageOnlyRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("image")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfImageOnlyRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("image", CheckStatus.Fail)]
    [InlineData("ocr", CheckStatus.NotEvaluated)]
    public async Task PartialReportNeverClaimsFullSearchabilityQualification(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(Build(scenario));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SharedBudgetIncludesImageOnlyFinding(int limit)
    {
        var fixture = await Fixture(Build("image"), limits: new() { MaxXmlFindings = limit });
        var result = await Run(fixture);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task KnownFailureSurvivesLaterDocumentLimit()
    {
        var fixture = await Fixture(Build("image"), secondPdf: true);
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture(Build("image")); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new FdaPdfImageOnlyRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token));
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "later-limit-image-only-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            if (_count++ > 0) throw new PdfInspectionLimitException("Later document limit.");
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

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
        public string SourceId => "fda5057";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string scenario, string? rawContent = null, string imageData = " ", string colorSpace = "/DeviceGray",
        string[]? contentParts = null, int filteredPart = -1, string? contentsOverride = null)
    {
        const string image = "q 100 0 0 100 20 20 cm /Im1 Do Q";
        const string text = "BT /F1 12 Tf 3 Tr (OCR text) Tj ET";
        var content = rawContent ?? (scenario switch
        {
            "blank" => "", "vector" => "0 0 100 100 re f", "ocr" => image + " " + text,
            "empty-text" => image + " BT /F1 12 Tf () Tj ET", "nested" => "/Fm1 Do",
            "two-images" => image + " " + image, "comment" => "% BT fake text\n" + image,
            "unknown-op" => image + " BX /UnknownThing unknown EX", _ => image
        });
        var secondPage = scenario is "two-pages" or "image-and-blank" or "image-and-text";
        var catalog = scenario switch { "form" => "/AcroForm << /Fields [] >>", "structure" => "/StructTreeRoot << /Type /StructTreeRoot >>", _ => "" };
        const string resources = "/Resources << /XObject << /Im1 5 0 R /Fm1 7 0 R >> /Font << /F1 6 0 R >> >>";
        var contents = contentsOverride ?? (contentParts is null ? "4 0 R" : "[" + string.Join(" ", contentParts.Select((_, index) => (index + 11).ToString(CultureInfo.InvariantCulture) + " 0 R")) + "]");
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R " + catalog + " >>",
            "<< /Type /Pages /Kids [3 0 R " + (secondPage ? "9 0 R" : "") + "] /Count " + (secondPage ? "2" : "1") + " >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " + resources + " /Contents " + contents + " " + (scenario == "annotation" ? "/Annots [8 0 R]" : "") + " >>",
            Stream(scenario == "filtered" ? Convert.ToHexString(Encoding.ASCII.GetBytes(content)) + ">" : content, scenario == "filtered" ? "/Filter /ASCIIHexDecode" : ""),
            Stream(imageData, "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace " + colorSpace + " /BitsPerComponent 8"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            Stream(image, "/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /XObject << /Im1 5 0 R >> >>"),
            "<< /Type /Annot /Subtype /Text /Rect [0 0 100 100] /Contents (Annotation text) >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " + resources + " /Contents 10 0 R >>",
            Stream(scenario == "image-and-blank" ? "" : scenario == "image-and-text" ? text : image)
        };
        if (contentParts is not null) objects = objects.Concat(contentParts.Select((part, index) =>
            Stream(index == filteredPart ? Convert.ToHexString(Encoding.ASCII.GetBytes(part)) + ">" : part,
                index == filteredPart ? "/Filter /ASCIIHexDecode" : ""))).ToArray();
        var output = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++) { offsets.Add(Encoding.ASCII.GetByteCount(output.ToString())); output.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(output.ToString()); output.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) output.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        output.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(output.ToString());
    }
    private static string Stream(string content, string entries = "") => "<< /Length " + Encoding.ASCII.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + " " + entries + " >>\nstream\n" + content + "\nendstream";
}
