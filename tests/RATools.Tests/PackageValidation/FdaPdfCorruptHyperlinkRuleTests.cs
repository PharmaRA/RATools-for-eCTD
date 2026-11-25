using System.Globalization;
using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfCorruptHyperlinkRuleTests
{
    private const string RuleId = "FDA47-5201";

    [Theory]
    [InlineData("broken", false, false, CheckStatus.Fail)]
    [InlineData("broken", true, false, CheckStatus.Fail)]
    [InlineData("broken", false, true, CheckStatus.Fail)]
    [InlineData("valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("missing", false, false, CheckStatus.NotEvaluated)]
    [InlineData("unselected-history", false, false, CheckStatus.NotEvaluated)]
    [InlineData("bookmark", false, false, CheckStatus.NotEvaluated)]
    [InlineData("open-action", false, false, CheckStatus.NotEvaluated)]
    [InlineData("uri", false, false, CheckStatus.NotEvaluated)]
    [InlineData("wrong-literal", false, false, CheckStatus.NotEvaluated)]
    [InlineData("literal-match", false, false, CheckStatus.Fail)]
    [InlineData("non-pdf-name", false, false, CheckStatus.NotEvaluated)]
    public async Task CorruptTargetRequiresExistingBoundPdfAndQualifiedHyperlink(string scenario, bool targetFirst, bool history, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, targetFirst, history);
        var result = await Run(fixture);
        Assert.Null(Assert.Single(result.Documents, item => item.LogicalPath == fixture.Source).Facts!.Basics.ParseError);
        if (scenario is "broken" or "literal-match")
            Assert.Equal(PdfContentProcessingStatus.InvalidContent, Assert.Single(result.Documents, item => item.LogicalPath == fixture.Target).Facts!.ContentProcessing);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal(fixture.Source, finding.Location.LogicalPath);
            Assert.Equal(fixture.Target, finding.Actual);
            Assert.Contains("page[1]", finding.Location.FieldPath);
            Assert.Contains("future submissions", finding.RecommendedAction);
            Assert.DoesNotContain("Replace", finding.RecommendedAction);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5200").Status);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(PdfContentProcessingStatus.Unknown, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.Encrypted, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.InputUnavailable, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.ResourceLimited, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.InvalidContent, CheckStatus.Fail)]
    public async Task ErrorMessageAloneDoesNotProveCorruptTarget(PdfContentProcessingStatus status, CheckStatus expected)
    {
        var fixture = await Fixture("broken");
        var result = await new PackagePdfValidator(new TargetInspector(fixture.Target, status), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture("broken", date: date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("source-hash")]
    [InlineData("target-hash")]
    [InlineData("missing-source")]
    [InlineData("missing-target")]
    [InlineData("target-pages")]
    [InlineData("target-encrypted")]
    [InlineData("target-no-error")]
    [InlineData("source-legacy")]
    [InlineData("source-pages")]
    [InlineData("source-count-conflict")]
    [InlineData("source-page-index")]
    [InlineData("label-only")]
    public async Task BothSidesRequireIndependentConsistentEvidence(string scenario)
    {
        var fixture = await Fixture("broken"); var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source); var target = documents.Single(item => item.LogicalPath == fixture.Target);
        if (scenario == "source-hash") source = source with { CapturedSha256 = new string('0', 64) };
        if (scenario == "target-hash") target = target with { CapturedSha256 = new string('0', 64) };
        if (scenario is "target-pages" or "target-encrypted" or "target-no-error")
        {
            var basics = target.Facts!.Basics with { PageCount = scenario == "target-pages" ? 1 : null,
                IsEncrypted = scenario == "target-encrypted", ParseError = scenario == "target-no-error" ? null : "opaque" };
            target = target with { Facts = new(basics, [], [], false, false, contentProcessing: PdfContentProcessingStatus.InvalidContent) };
        }
        if (scenario is "source-legacy" or "source-pages" or "source-count-conflict" or "source-page-index" or "label-only")
        {
            var facts = source.Facts!;
            source = source with { Facts = new(facts.Basics, facts.Links, [], true, true,
                contentProcessing: scenario == "source-legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
                qualifiedPageCount: scenario == "source-pages" ? null : scenario == "source-count-conflict" ? 2 : 1,
                hyperlinkFiles: scenario == "label-only" ? [] : [new("page[1]/annotation[1]/A/F", scenario == "source-page-index" ? 2 : 1, "b-target.pdf")]) };
        }
        var run = new FdaPdfCorruptHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        if (scenario != "missing-source") run.Observe(source, 10);
        if (scenario != "missing-target") run.Observe(target, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(false, 0, CheckStatus.NotEvaluated)]
    [InlineData(false, 1, CheckStatus.Fail)]
    [InlineData(true, 0, CheckStatus.NotEvaluated)]
    [InlineData(true, 1, CheckStatus.Fail)]
    public async Task EitherOrderRespectsAllowanceAndDoesNotRepeat(bool targetFirst, int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("broken"); var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source); var target = documents.Single(item => item.LogicalPath == fixture.Target);
        var run = new FdaPdfCorruptHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, remaining); run.Observe(targetFirst ? source : target, remaining);
        run.Observe(source, 10); run.Observe(target, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("broken"); var documents = (await Run(fixture)).Documents;
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfCorruptHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            foreach (var document in documents) run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(6, CheckStatus.NotEvaluated)]
    [InlineData(7, CheckStatus.Fail)]
    public async Task SharedBudgetOnlyRetainsAllocatedFindings(int limit, CheckStatus expected)
    {
        var result = await Run(await Fixture("broken", limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == "FDA47-3102").Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task LaterLimitCannotEraseKnownCorruptHyperlink()
    {
        var fixture = await Fixture("later-limit");
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task MissingArchivedSourceRemainsUnknown()
    {
        var result = await Run(await Fixture("broken", missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task NoCurrentPdfIsNotApplicable()
    {
        var result = await Run(await Fixture("no-pdf"));
        Assert.Equal(CheckStatus.NotApplicable, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialReportRetainsLocatedFailure()
    {
        var fixture = await Fixture("broken");
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        public string EngineVersion => "later-limit-hyperlink-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken) =>
            logicalPath.EndsWith("z-later.pdf", StringComparison.Ordinal) ? throw new PdfInspectionLimitException("Later limit") :
                new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
    }

    private sealed class TargetInspector(string target, PdfContentProcessingStatus status) : IPackagePdfInspector
    {
        public string EngineVersion => "typed-hyperlink-target-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken) =>
            logicalPath == target ? new(new PdfInspectionResult(null, false, null, false, null, [], false, [], ParseError: "opaque failure"), [], [], false, false, contentProcessing: status) :
                new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
    }
    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context, string Source, string Target);
    private static async Task<FixtureData> Fixture(string scenario, bool targetFirst = false, bool history = false, string? date = "2026-11-26", PackageReadLimits? limits = null, bool missingSource = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, item => item.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var app = Guid.NewGuid(); var readLimits = limits ?? new();
        var source = targetFirst ? "0001/z-source.pdf" : "0001/a-source.pdf";
        var targetName = scenario == "literal-match" ? "b%2Epdf.pdf" : scenario == "non-pdf-name" ? "b-target.txt" : "b-target.pdf";
        var target = (history || scenario == "unselected-history" ? "0000/" : "0001/") + targetName;
        var specification = (history || scenario == "unselected-history" ? "../0000/" : "") + (scenario == "wrong-literal" ? "b%2Dtarget.pdf" : targetName);
        var action = scenario == "uri" ? "<< /S /URI /URI (" + specification + ") >>" : "<< /S /GoToR /F (" + specification + ") /D [0 /Fit] >>";
        var sourceBytes = scenario == "bookmark" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (target) /Parent 7 0 R /A " + action + " >>") :
            scenario == "open-action" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action) : PdfPigPackageInspectorTests.Build("/A " + action);
        var targetBytes = scenario == "valid" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]") : "not a PDF"u8.ToArray();
        var additions = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (scenario != "no-pdf") additions[source] = sourceBytes;
        if (!history && scenario is not ("missing" or "unselected-history" or "no-pdf")) additions[target] = targetBytes;
        if (scenario == "later-limit") additions["0001/z-later.pdf"] = sourceBytes;
        var current = new Capture(app, "0001", additions, readLimits);
        var old = history ? new Capture(app, "0000", new(StringComparer.Ordinal) { [target] = targetBytes }, readLimits) : null;
        var baseline = new HistoryBaselineManifest(app, "0001", old is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, old.SourceId, old.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
        var packages = new PackageInputSet(current, baseline, old is null ? [] : [old]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context, source, target);
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, Dictionary<string, byte[]> additions, PackageReadLimits limits)
        {
            Limits = limits;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            foreach (var file in additions) _files.Add(file.Key, file.Value);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5201-" + Manifest.SequenceNumber;
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
