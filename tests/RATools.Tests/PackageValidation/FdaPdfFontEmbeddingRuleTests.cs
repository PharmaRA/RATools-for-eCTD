using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfFontEmbeddingRuleTests
{
    private const string RuleId = "FDA47-5005";

    [Theory]
    [InlineData("Helvetica", "", CheckStatus.Fail)]
    [InlineData("Times-Roman", "", CheckStatus.Fail)]
    [InlineData("Courier", "", CheckStatus.Fail)]
    [InlineData("ArialMT", "", CheckStatus.NotEvaluated)]
    [InlineData("Symbol", "", CheckStatus.NotEvaluated)]
    [InlineData("ZapfDingbats", "", CheckStatus.NotEvaluated)]
    [InlineData("Helvetica", "/FontDescriptor << /Type /FontDescriptor /FontName /Helvetica >>", CheckStatus.Fail)]
    [InlineData("Helvetica", "/FontDescriptor << /Type /FontDescriptor /FontName /Other >>", CheckStatus.NotEvaluated)]
    [InlineData("Helvetica", "/FontDescriptor null", CheckStatus.NotEvaluated)]
    [InlineData("Helvetica", "/FontDescriptor 99 0 R", CheckStatus.NotEvaluated)]
    [InlineData("Helvetica", "/FontDescriptor << /FontFile2 null >>", CheckStatus.NotEvaluated)]
    [InlineData("ABCDEF+Helvetica", "/FontDescriptor << /FontFile 6 0 R >>", CheckStatus.NotEvaluated)]
    public async Task RealUsedFontsRequireIdentityAndDefiniteMissingEmbedding(string name, string descriptor, CheckStatus expected)
    {
        var fixture = await Fixture(Build(name, descriptor));
        var pdf = await Run(fixture);
        Assert.Null(Assert.Single(pdf.Documents).Facts!.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, Assert.Single(pdf.Documents).Facts!.ContentProcessing);
        var check = Assert.Single(pdf.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Contains(name, finding.Actual);
            Assert.Contains("page[1]", finding.Location.FieldPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-20", CheckStatus.NotEvaluated)]
    [InlineData("2016-09-21", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task FixedPdfSpecificationIsNotAppliedRetroactively(string? date, CheckStatus expected)
    {
        var fixture = await Fixture(Build(), date);
        Assert.Equal(expected, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c")]
    [InlineData("113faa128d34ca5c2b99184da2cf4da2e4359403204289892720c1f73afcae2a")]
    public async Task BothArchivedOfficialSourcesAreRequired(string missingHash)
    {
        var fixture = await Fixture(Build(), missingHash: missingHash);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceAliasOrUnusedFontIsNotAUsedNonStandardFont(bool unused)
    {
        var fixture = await Fixture(Build(null, content: unused ? "" : "BT /F1 12 Tf (text) Tj ET", unused: unused));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task NoCurrentPdfIsNotApplicable()
    {
        var fixture = await Fixture(null);
        Assert.Equal(CheckStatus.NotApplicable, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("CustomRegulatoryFont")]
    [InlineData("TimesNewRomanPSMT")]
    [InlineData("CourierNewPSMT")]
    public async Task UnsupportedType1FixtureCannotSupplyFontEvidence(string name)
    {
        var result = await Run(await Fixture(Build(name)));
        Assert.NotNull(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("BT /F1 12 Tf () Tj ET", CheckStatus.NotEvaluated)]
    [InlineData("BT /F1 12 Tf [(text) 20] TJ ET", CheckStatus.Fail)]
    [InlineData("BT /F1 12 Tf [20 ()] TJ ET", CheckStatus.NotEvaluated)]
    [InlineData("BT /F1 12 Tf (text) ' ET", CheckStatus.Fail)]
    [InlineData("BT /F1 12 Tf 0 0 (text) \" ET", CheckStatus.Fail)]
    [InlineData("q BT /F1 12 Tf (text) Tj ET Q", CheckStatus.Fail)]
    public async Task OnlyActualNonEmptyTextUseSuppliesMissingFontEvidence(string content, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(content: content)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("parse")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("type3")]
    [InlineData("alias")]
    [InlineData("no-facts")]
    public async Task UnqualifiedFrozenEvidenceCannotAuthorizeFailure(string scenario)
    {
        var fixture = await Fixture(Build());
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath.EndsWith("report.pdf", StringComparison.Ordinal));
        var observation = new PdfFontObservation("page[1]/Resources/Font/F1", scenario == "alias" ? "F1" : "Helvetica", scenario == "type3" ? "Type3" : "Type1", scenario == "unknown" ? PdfFontEmbeddingStatus.Unknown : PdfFontEmbeddingStatus.Missing);
        var basics = new PdfInspectionResult("1.4", false, false, true, false, ["Helvetica"], false, [], PageCount: 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            fonts: scenario == "duplicate" ? [observation, observation with { Embedding = PdfFontEmbeddingStatus.Unknown }] : [observation]);
        var run = new FdaPdfFontEmbeddingRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, scenario == "no-facts" ? null : facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceAndInterruptedFinish(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build()); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfFontEmbeddingRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining);
        Assert.Equal(expected, run.Finish(true)!.Status);
        Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrEstablishedFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build()); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfFontEmbeddingRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Fact]
    public async Task LegacyEmbeddedBooleanCannotProducePass()
    {
        var fixture = await Fixture(Build());
        var result = await new PackagePdfValidator(new LegacyInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(true, CheckStatus.Fail)]
    [InlineData(false, CheckStatus.NotEvaluated)]
    public async Task PartialReportCannotBecomeCompleteCoverage(bool failure, CheckStatus expected)
    {
        var fixture = await Fixture(Build(failure ? "Helvetica" : "ArialMT"));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private sealed class LegacyInspector : IPackagePdfInspector
    {
        public string EngineVersion => "legacy-font-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
            => new(new PdfInspectionResult("1.4", false, false, true, true, [], false, [], PageCount: 1), [], [], true, true);
    }

    [Theory]
    [InlineData("BT /F2 12 Tf (standard) Tj ET", 0)]
    [InlineData("BT /F1 12 Tf (first) Tj /F3 12 Tf (second) Tj ET", 2)]
    [InlineData("BT /F1 12 Tf (first) Tj (again) Tj ET", 1)]
    [InlineData("BT /F2 12 Tf ET q BT /F1 12 Tf ET Q BT (standard) Tj ET", 0)]
    public async Task ActualResourceSelectionAndGraphicsStateAreRetained(string content, int count)
    {
        var result = await Run(await Fixture(Build(content: content, resources: "/F2 << /Type /Font /Subtype /Type1 /BaseFont /Symbol >> /F3 << /Type /Font /Subtype /Type1 /BaseFont /Courier >>")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
    }

    [Fact]
    public async Task ExtGStateFontSelectionRemainsUnknown()
    {
        var result = await Run(await Fixture(Build(content: "/GS gs BT (standard) Tj ET", pageResources: "/ExtGState << /GS << /Type /ExtGState /Font [7 0 R 12] >> >>")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("BT /F1 12 Tf (known) Tj /F2 12 Tf (unknown) Tj ET")]
    [InlineData("BT /F2 12 Tf (unknown) Tj /F1 12 Tf (known) Tj ET")]
    public async Task OneBrokenDescriptorCannotEraseAnotherKnownFont(string content)
    {
        var result = await Run(await Fixture(Build(content: content, resources: "/F2 << /Type /Font /Subtype /Type1 /BaseFont /Courier /FontDescriptor 99 0 R >>")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("Helvetica", Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SharedFindingLimitRetainsFontFailure(int limit)
    {
        var fixture = await Fixture(Build(), limits: new PackageReadLimits { MaxXmlFindings = limit });
        var result = await Run(fixture);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task EarlierDocumentFailureSurvivesLaterInspectorLimit()
    {
        var fixture = await Fixture(Build(), secondPdf: true);
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task PreCancelledCallDoesNotReadFonts()
    {
        var fixture = await Fixture(Build()); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new FdaPdfFontEmbeddingRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token));
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "later-limit-font-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            if (_count++ > 0) throw new PdfInspectionLimitException("Later document limit.");
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? bytes, string? date = "2026-11-25", string? missingHash = null, PackageReadLimits? limits = null, bool secondPdf = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingHash is not null)
        {
            Assert.Contains(snapshot.Resources, item => item.Sha256 == missingHash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != missingHash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != missingHash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(bytes, limits, secondPdf);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(byte[]? pdf, PackageReadLimits? limits, bool secondPdf)
        {
            Limits = limits ?? new();
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.Where(file => !file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (pdf is not null) _files["0001/report.pdf"] = pdf;
            if (secondPdf && pdf is not null) _files["0001/z-other.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5005";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // Hand-authored PDF objects; unused resources deliberately have no text operator.
    internal static byte[] Build(string? name = "Helvetica", string descriptor = "", string content = "BT /F1 12 Tf (text) Tj ET", bool unused = false, string resources = "", string pageResources = "")
    {
        var font = "<< /Type /Font /Subtype /Type1 " + (name is null ? "" : "/BaseFont /" + name) + " " + descriptor + " >>";
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R " + resources + (unused ? "/Unused << /Type /Font /Subtype /Type1 /BaseFont /CustomUnused >>" : "") + " >> " + pageResources + " >> /Contents 5 0 R >>",
            font,
            "<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream",
            "<< /Length 3 >>\nstream\nxyz\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>"
        };
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(text.ToString()));
            text.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(text.ToString());
        text.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
