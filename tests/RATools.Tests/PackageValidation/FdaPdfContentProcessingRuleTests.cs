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
using UglyToad.PdfPig.Exceptions;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfContentProcessingRuleTests
{
    private const string RuleId = "FDA47-3102";

    [Theory]
    [InlineData("valid", PdfContentProcessingStatus.Processed)]
    [InlineData("broken", PdfContentProcessingStatus.InvalidContent)]
    [InlineData("io", PdfContentProcessingStatus.InputUnavailable)]
    [InlineData("encrypted", PdfContentProcessingStatus.Encrypted)]
    [InlineData("depth", PdfContentProcessingStatus.ResourceLimited)]
    [InlineData("unknown", PdfContentProcessingStatus.Unknown)]
    [InlineData("wrapped-format", PdfContentProcessingStatus.Unknown)]
    public void AdapterPreservesFailureCategoryWithoutGuessingFromMessage(string scenario, PdfContentProcessingStatus expected)
    {
        using Stream stream = scenario is "valid" or "broken" ? new MemoryStream(scenario == "valid" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]") : "not a pdf"u8.ToArray()) : new FailingStream(scenario);
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        if (scenario == "valid") { Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.Basics.PageCount); }
        else Assert.NotNull(facts.Basics.ParseError);
        Assert.Equal(expected, facts.ContentProcessing);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Pass)]
    [InlineData("broken", CheckStatus.Fail)]
    [InlineData("io", CheckStatus.NotEvaluated)]
    [InlineData("encrypted", CheckStatus.NotEvaluated)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("unknown", CheckStatus.NotEvaluated)]
    [InlineData("legacy-error", CheckStatus.NotEvaluated)]
    [InlineData("no-pdf", CheckStatus.NotApplicable)]
    public async Task CurrentPdfClassificationReachesTheIndependentRule(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        IPackagePdfInspector inspector = scenario == "legacy-error" ? new FixedInspector("legacy") : new PdfPigPackageInspector();
        var result = await new PackagePdfValidator(inspector, standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(PdfContentProcessingStatus.InvalidContent, true, CheckStatus.Fail)]
    [InlineData(PdfContentProcessingStatus.InvalidContent, false, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.Unknown, true, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.Encrypted, true, CheckStatus.NotEvaluated)]
    public async Task FrozenCategoriesRequireExactCaptureBinding(PdfContentProcessingStatus status, bool matchingHash, CheckStatus expected)
    {
        var fixture = await Fixture("broken");
        var file = Assert.Single(fixture.Input.Packages.Target.Manifest.Files, item => item.LogicalPath.EndsWith("report.pdf", StringComparison.Ordinal));
        var facts = FailureFacts(status);
        var result = Direct(fixture, [new(file.LogicalPath, facts, matchingHash ? file.Sha256 : new string('0', 64))])!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, result.Findings.Count);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var fixture = await Fixture("broken", date);
        Assert.Equal(expected, Direct(fixture, Evidence(fixture))!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingBudgetIsExact(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("broken"); Assert.Equal(expected, Direct(fixture, Evidence(fixture), remaining)!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesProgress(bool afterFailure)
    {
        var fixture = await Fixture("broken"); using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, Evidence(fixture), progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("legacy")]
    [InlineData("missing")]
    [InlineData("contradictory")]
    [InlineData("missing-source")]
    public async Task UnqualifiedOrContradictoryFactsRemainUnknown(string scenario)
    {
        var fixture = await Fixture("broken", missingSource: scenario == "missing-source");
        var file = Assert.Single(fixture.Input.Packages.Target.Manifest.Files, item => item.LogicalPath.EndsWith("report.pdf", StringComparison.Ordinal));
        var facts = scenario == "legacy" ? new PackagePdfFacts(FailureFacts(PdfContentProcessingStatus.Unknown).Basics, [], [], false, false)
            : scenario == "contradictory" ? new PackagePdfFacts(new PdfInspectionResult("1.4", false, false, true, true, [], false, [], PageCount: 1), [], [], true, true, contentProcessing: PdfContentProcessingStatus.InvalidContent)
            : FailureFacts(PdfContentProcessingStatus.InvalidContent);
        var document = new PackagePdfDocument(file.LogicalPath, facts, file.Sha256);
        var check = Direct(fixture, scenario == "missing" ? [] : scenario == "duplicate" ? [document, document] : [document])!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task KnownFailureSurvivesLaterResourceLimit()
    {
        var fixture = await Fixture("two-pdf");
        var result = await new PackagePdfValidator(new FixedInspector("then-limit"), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "PDF_INSPECTION_LIMIT");
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("0001/report.pdf", Assert.Single(check.Findings).Location.LogicalPath);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("broken", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("no-pdf", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    public async Task PartialReportPreservesContentProcessingBoundary(string scenario, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Pdf.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("broken"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, Evidence(fixture), token: cancellation.Token));
    }

    private sealed class FixedInspector(string scenario) : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "typed-boundary-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scenario == "then-limit" && _count++ > 0) throw new PdfInspectionLimitException("Injected resource limit after known failure.");
            return scenario == "legacy" ? new(FailureFacts(PdfContentProcessingStatus.Unknown).Basics, [], [], false, false) : FailureFacts(PdfContentProcessingStatus.InvalidContent);
        }
    }

    private static PackagePdfFacts FailureFacts(PdfContentProcessingStatus status) => new(new PdfInspectionResult(null, false, null, false, null, [], false, [], ParseError: "opaque failure"), [], [], false, false, contentProcessing: status);
    private static PackagePdfDocument[] Evidence(FixtureData fixture) => fixture.Input.Packages.Target.Manifest.Files.Where(file => file.LogicalPath.EndsWith(".pdf", StringComparison.Ordinal)).Select(file => new PackagePdfDocument(file.LogicalPath, FailureFacts(PdfContentProcessingStatus.InvalidContent), file.Sha256)).ToArray();
    private static RuleCheckResult? Direct(FixtureData fixture, IReadOnlyList<PackagePdfDocument> documents, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaPdfContentProcessingRule.Inspect(fixture.Input, fixture.Files, documents, fixture.Context, remaining, token, progress);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);

    private static async Task<FixtureData> Fixture(string scenario, string? date = "2026-11-25", bool missingSource = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, item => item.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var target = new Capture(scenario); var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        var files = await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle);
        target.InjectFailures = true;
        return new(input, files, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _scenario;
        public Capture(string scenario)
        {
            _scenario = scenario;
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.Where(file => !file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (scenario != "no-pdf") _files["0001/report.pdf"] = scenario == "valid" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]") : Encoding.ASCII.GetBytes("not a pdf");
            if (scenario == "two-pdf") _files["0001/z-other.pdf"] = Encoding.ASCII.GetBytes("not a pdf");
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public bool InjectFailures { get; set; }
        public string SourceId => "fda3102";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => InjectFailures && path.EndsWith("report.pdf", StringComparison.Ordinal) && _scenario is "io" or "encrypted" or "depth" or "unknown" or "legacy-error" ? new FailingStream(_scenario) : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    // Injected reader failures verify the adapter boundary, not real encrypted or deep PDF qualification.
    private sealed class FailingStream(string scenario) : MemoryStream(new byte[64], false)
    {
        private Exception Error() => scenario switch
        {
            "io" => new IOException("opaque failure"), "encrypted" => new PdfDocumentEncryptedException("opaque failure"),
            "depth" => (Exception)Activator.CreateInstance(typeof(PdfDocumentStackDepthException), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [1], System.Globalization.CultureInfo.InvariantCulture)!,
            "wrapped-format" => new PdfDocumentFormatException("opaque failure", new IOException("opaque failure")),
            _ => new InvalidOperationException("opaque failure")
        };
        public override int Read(byte[] buffer, int offset, int count) => throw Error();
        public override int Read(Span<byte> buffer) => throw Error();
        public override int ReadByte() => throw Error();
    }
}
