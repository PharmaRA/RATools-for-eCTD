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

public sealed class FdaPdfVersionRuleTests
{
    private const string RuleId = "FDA47-5035";

    [Theory]
    [InlineData("1.3", "1.4")]
    [InlineData("1.3", "1.7")]
    [InlineData("1.7", "1.4")]
    [InlineData("1.4", "1.4")]
    [InlineData("1.0", "1.4")]
    [InlineData("1.4", "1.5")]
    [InlineData("1.4", "1.6")]
    [InlineData("1.5", "1.7")]
    [InlineData("1.6", "1.5")]
    public async Task DirectCatalogNameSelectsTheLaterOrdinaryVersion(string header, string catalog)
    {
        var result = await Run(await Fixture(Build(header, "/Version /" + catalog)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Equal(header, facts.VersionDeclaration!.HeaderVersion);
        Assert.True(facts.VersionDeclaration.HasCatalogVersion);
        Assert.Equal(catalog, facts.VersionDeclaration.CatalogVersion);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("/Version null", null)]
    [InlineData("/Version (1.7)", null)]
    [InlineData("/Version 1.7", null)]
    [InlineData("/Version 99 0 R", null)]
    [InlineData("/Version << >>", null)]
    [InlineData("/Version /1.30", "1.30")]
    [InlineData("/Version /2.0", "2.0")]
    [InlineData("/Version /1.3", "1.3")]
    [InlineData("/Version /1.7 /Extensions null", "1.7")]
    [InlineData("/Version /1.7 /Metadata null", "1.7")]
    public async Task UnqualifiedCatalogValuesAreNotOrdinaryVersionEvidence(string catalog, string? rawName)
    {
        var result = await Run(await Fixture(Build("1.3", catalog)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.True(facts.VersionDeclaration!.HasCatalogVersion);
        Assert.Equal(rawName, facts.VersionDeclaration.CatalogVersion);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("1.3", "1.3", true, "1.7", CheckStatus.Pass)]
    [InlineData("1.7", "1.3", true, "1.7", CheckStatus.Pass)]
    [InlineData("1.5", "1.3", true, "1.7", CheckStatus.NotEvaluated)]
    [InlineData("1.7", "1.3", true, null, CheckStatus.NotEvaluated)]
    [InlineData("1.3", "1.3", false, "1.7", CheckStatus.NotEvaluated)]
    [InlineData("1.3", "1.30", true, "1.7", CheckStatus.NotEvaluated)]
    [InlineData("1.3", "1.3", true, "1.70", CheckStatus.NotEvaluated)]
    [InlineData("1.3", "1.3", true, "2.0", CheckStatus.NotEvaluated)]
    public async Task CatalogEvidenceMustAgreeWithRawHeaderAndParsedVersion(string parsed, string header, bool present, string? catalog, CheckStatus expected)
    {
        var fixture = await Fixture(Build("1.3"));
        var file = Assert.Single(fixture.Input.Packages.Target.Manifest.Files, item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult(parsed, false, false, true, true, [], false, [], PageCount: 1);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
            versionDeclaration: new(header, present, false, false, catalog));
        var run = new FdaPdfVersionRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(expected, check.Status); Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task CatalogVersionComponentPassRemainsPartialInReport()
    {
        var fixture = await Fixture(Build("1.3", "/Version /1.7"));
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Pdf.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("1.0", "", CheckStatus.Fail)]
    [InlineData("1.3", "", CheckStatus.Fail)]
    [InlineData("1.4", "", CheckStatus.Pass)]
    [InlineData("1.5", "", CheckStatus.Pass)]
    [InlineData("1.6", "", CheckStatus.Pass)]
    [InlineData("1.7", "", CheckStatus.Pass)]
    [InlineData("1.3", "/Version /1.7", CheckStatus.Pass)]
    [InlineData("1.7", "/Version /2.0", CheckStatus.NotEvaluated)]
    [InlineData("1.7", "/Version (2.0)", CheckStatus.NotEvaluated)]
    [InlineData("1.7", "/Version null", CheckStatus.NotEvaluated)]
    [InlineData("1.7", "/Extensions << >>", CheckStatus.NotEvaluated)]
    [InlineData("1.3", "/Metadata null", CheckStatus.NotEvaluated)]
    public async Task DeclaredVersionRequiresUnambiguousOriginalEvidence(string version, string catalog, CheckStatus expected)
    {
        var fixture = await Fixture(Build(version, catalog));
        var result = await Run(fixture);
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Equal(version, finding.Actual);
            Assert.Contains("No corrective action", finding.RecommendedAction);
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
    public async Task VersionPolicyIsDateQualified(string? date, CheckStatus expected)
    {
        var fixture = await Fixture(Build("1.3"), date);
        Assert.Equal(expected, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c")]
    [InlineData("113faa128d34ca5c2b99184da2cf4da2e4359403204289892720c1f73afcae2a")]
    public async Task BothArchivedSourcesAreRequired(string hash)
    {
        var fixture = await Fixture(Build("1.3"), missingHash: hash);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task LegacyVersionStringDoesNotAuthorizeRegulatoryConclusion()
    {
        var fixture = await Fixture(Build("1.3"));
        var result = await new PackagePdfValidator(new LegacyInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false, CheckStatus.NotApplicable)]
    [InlineData(true, CheckStatus.NotEvaluated)]
    public async Task NoPdfAndBrokenPdfAreDifferent(bool broken, CheckStatus expected)
    {
        var fixture = await Fixture(broken ? "not a PDF"u8.ToArray() : null);
        Assert.Equal(expected, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task UnsupportedParserVersionCannotSupplyQualifiedDeclaration()
    {
        var result = await Run(await Fixture(Build("2.0")));
        Assert.NotNull(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("1.4", "", false, false, false)]
    [InlineData("1.3", "/Version /1.7", true, false, false)]
    [InlineData("1.7", "/Extensions null", false, true, false)]
    [InlineData("1.4", "/Metadata null", false, false, true)]
    public void AdapterRetainsOriginalHeaderAndCatalogKeyPresence(string version, string catalog, bool overridden, bool extensions, bool metadata)
    {
        using var stream = new MemoryStream(Build(version, catalog));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(new PdfVersionDeclaration(version, overridden, extensions, metadata, overridden ? "1.7" : null), facts.VersionDeclaration);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("missing-declaration")]
    [InlineData("contradictory")]
    [InlineData("invalid-version")]
    [InlineData("encrypted")]
    [InlineData("missing-facts")]
    [InlineData("catalog-version")]
    [InlineData("extensions")]
    [InlineData("metadata")]
    public async Task UnqualifiedEvidenceNeverProvesVersionMismatch(string scenario)
    {
        var fixture = await Fixture(Build("1.3"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult(scenario == "contradictory" ? "1.7" : "1.3", scenario == "encrypted", false, true, true, [], false, [], PageCount: 1);
        var declaration = new PdfVersionDeclaration(scenario == "invalid-version" ? "1.30" : "1.3", scenario == "catalog-version", scenario == "extensions", scenario == "metadata");
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            versionDeclaration: scenario == "missing-declaration" ? null : declaration);
        var run = new FdaPdfVersionRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, scenario == "missing-facts" ? null : facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceRetainsFailureWhenInterrupted(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("1.3")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfVersionRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining);
        Assert.Equal(expected, run.Finish(true)!.Status);
        Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesUnknownOrEstablishedFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("1.3")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfVersionRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("interrupted")]
    public async Task PartialObservationCannotBecomePass(string scenario)
    {
        var fixture = await Fixture(Build("1.4")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfVersionRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        if (scenario != "missing") run.Observe(document, 10);
        if (scenario == "duplicate") run.Observe(document, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(scenario == "interrupted")!.Status);
    }

    [Theory]
    [InlineData("1.3", CheckStatus.Fail)]
    [InlineData("1.4", CheckStatus.NotEvaluated)]
    public async Task ReportKeepsPartialCoverage(string version, CheckStatus expected)
    {
        var fixture = await Fixture(Build(version));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture(Build("1.3")); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new FdaPdfVersionRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SharedFindingBudgetIncludesVersionResult(int limit)
    {
        var fixture = await Fixture(Build("1.3"), limits: new() { MaxXmlFindings = limit });
        var result = await Run(fixture);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task KnownMismatchSurvivesLaterDocumentLimit()
    {
        var fixture = await Fixture(Build("1.3"), secondPdf: true);
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "later-limit-version-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            if (_count++ > 0) throw new PdfInspectionLimitException("Later document resource limit.");
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

    private sealed class LegacyInspector : IPackagePdfInspector
    {
        public string EngineVersion => "legacy-version-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
            => new(new PdfInspectionResult("1.3", false, false, true, true, [], false, [], PageCount: 1), [], [], true, true);
    }

    private static byte[] Build(string version, string catalog = "") => Encoding.ASCII.GetBytes(
        Encoding.ASCII.GetString(PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", catalog)).Replace("%PDF-1.4", "%PDF-" + version, StringComparison.Ordinal));
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
            Limits = limits;
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (pdf is not null) _files["0001/report.pdf"] = pdf;
            if (pdf is not null && secondPdf) _files["0001/z-other.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5035";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
