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

public sealed class FdaPdfExternalHyperlinkRuleTests
{
    private const string RuleId = "FDA47-5205";

    [Theory]
    [InlineData("relative.pdf", "https://base.invalid/docs/")]
    [InlineData("../review.pdf", "https://base.invalid/docs/")]
    [InlineData("chapter/one.html", "http://base.invalid/")]
    public async Task ExplicitWebBaseQualifiesSimpleRelativeHyperlink(string uri, string basis)
    {
        var result = await Run(await Fixture(Build(uri, catalog: "/URI << /Base (" + basis + ") >>")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("Web", Assert.Single(check.Findings).Actual);
        Assert.Equal(PdfExternalHyperlinkKind.Web, Assert.Single(facts.ExternalHyperlinks).Kind);
        Assert.DoesNotContain("base.invalid", System.Text.Json.JsonSerializer.Serialize(check.Findings));
        Assert.DoesNotContain("base.invalid", System.Text.Json.JsonSerializer.Serialize(facts.ExternalHyperlinks));
    }

    [Theory]
    [InlineData("relative.pdf", "")]
    [InlineData("relative.pdf", "/URI null")]
    [InlineData("relative.pdf", "/URI (https://base.invalid/)")]
    [InlineData("relative.pdf", "/URI << >>")]
    [InlineData("relative.pdf", "/URI << /Base null >>")]
    [InlineData("relative.pdf", "/URI << /Base /Invalid >>")]
    [InlineData("relative.pdf", "/URI << /Base (relative/) >>")]
    [InlineData("relative.pdf", "/URI << /Base (ftp://base.invalid/) >>")]
    [InlineData("relative.pdf", "/URI << /Base (mailto:review@base.invalid) >>")]
    [InlineData("relative.pdf", "/URI << /Base (https://user@base.invalid/) >>")]
    [InlineData("relative.pdf", "/URI << /Base (https://base.invalid/a%2Fb/) >>")]
    [InlineData("relative.pdf", "/URI << /Base (https://base.invalid/?q=x) >>")]
    [InlineData("relative.pdf", "/URI << /Base (https://base.invalid/#part) >>")]
    [InlineData("relative.pdf", "/URI << /Base ( https://base.invalid/) >>")]
    [InlineData("/root.pdf", "/URI << /Base (https://base.invalid/) >>")]
    [InlineData("//other.invalid/a", "/URI << /Base (https://base.invalid/) >>")]
    [InlineData("relative%2Epdf", "/URI << /Base (https://base.invalid/) >>")]
    [InlineData("relative.pdf#part", "/URI << /Base (https://base.invalid/) >>")]
    [InlineData("relative.pdf?q=x", "/URI << /Base (https://base.invalid/) >>")]
    [InlineData("relative file.pdf", "/URI << /Base (https://base.invalid/) >>")]
    public async Task RelativeUriRequiresQualifiedBaseAndSupportedSyntax(string uri, string catalog)
    {
        var result = await Run(await Fixture(Build(uri, catalog: catalog)));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError); Assert.Equal(1, facts.QualifiedPageCount);
        Assert.Empty(facts.ExternalHyperlinks);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("wrong-page")]
    [InlineData("conflict")]
    [InlineData("next-action")]
    [InlineData("wrong-type")]
    [InlineData("is-map")]
    [InlineData("hidden")]
    public async Task WebBaseCannotBypassQualifiedLinkIdentity(string scenario)
    {
        var result = await Run(await Fixture(Build("relative.pdf", scenario, "/URI << /Base (https://base.invalid/) >>")));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("https://example.invalid/document", "link", CheckStatus.Fail)]
    [InlineData("http://example.invalid/", "link", CheckStatus.Fail)]
    [InlineData("HTTPS://example.invalid/", "link", CheckStatus.Fail)]
    [InlineData("mailto:review@example.invalid", "link", CheckStatus.Fail)]
    [InlineData("https://example.invalid/document", "base", CheckStatus.Fail)]
    [InlineData("relative.pdf", "link", CheckStatus.NotEvaluated)]
    [InlineData("relative.pdf", "base", CheckStatus.Fail)]
    [InlineData("#page=1", "link", CheckStatus.NotEvaluated)]
    [InlineData("https://", "link", CheckStatus.NotEvaluated)]
    [InlineData("mailto:", "link", CheckStatus.NotEvaluated)]
    [InlineData("ftp://example.invalid/", "link", CheckStatus.NotEvaluated)]
    [InlineData("https%3A//example.invalid/", "link", CheckStatus.NotEvaluated)]
    [InlineData(" https://example.invalid/", "link", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "bookmark", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "open-action", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "wrong-page", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "conflict", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "next-action", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "wrong-type", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "is-map", CheckStatus.NotEvaluated)]
    [InlineData("https://example.invalid/document", "hidden", CheckStatus.NotEvaluated)]
    public async Task ExternalUriRequiresQualifiedHyperlinkAndAbsoluteSupportedAddress(string uri, string scenario, CheckStatus expected)
    {
        var result = await Run(await Fixture(Build(uri, scenario)));
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
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
        var result = await Run(await Fixture(Build("https://example.invalid/"), date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task RegulatoryEvidenceRetainsCategoryNotOriginalAddress()
    {
        const string uri = "https://synthetic-user@example.invalid/path?synthetic-query=example";
        var result = await Run(await Fixture(Build(uri)));
        var finding = Assert.Single(Assert.Single(result.Checks, item => item.RuleId == RuleId).Findings);
        Assert.Equal("Web", finding.Actual);
        Assert.DoesNotContain("synthetic-", System.Text.Json.JsonSerializer.Serialize(finding));
        var observation = Assert.Single(Assert.Single(result.Documents).Facts!.ExternalHyperlinks);
        Assert.Equal(PdfExternalHyperlinkKind.Web, observation.Kind);
        Assert.DoesNotContain("synthetic-", System.Text.Json.JsonSerializer.Serialize(observation));
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
    [InlineData("unknown-kind")]
    [InlineData("invalid-kind")]
    [InlineData("label-only")]
    public async Task UnboundOrContradictoryFactsDoNotProveExternalHyperlink(string scenario)
    {
        var fixture = await Fixture(Build("https://example.invalid/"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [],
            PageCount: scenario == "no-pages" ? null : 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [new("page[1]/URI", 1, new(Uri: "https://example.invalid/"))], [], true, true,
            contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            qualifiedPageCount: scenario == "unqualified-pages" ? null : scenario == "page-conflict" ? 2 : 1,
            externalHyperlinks: scenario == "label-only" ? [] : [new("page[1]/annotation[1]/A/URI", scenario == "wrong-page" ? 2 : 1,
                scenario == "unknown-kind" ? PdfExternalHyperlinkKind.Unknown : scenario == "invalid-kind" ? (PdfExternalHyperlinkKind)99 : PdfExternalHyperlinkKind.Web)]);
        var run = new FdaPdfExternalHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, false)]
    [InlineData(1, CheckStatus.Fail, false)]
    [InlineData(0, CheckStatus.NotEvaluated, true)]
    [InlineData(1, CheckStatus.Fail, true)]
    public async Task FindingAllowanceAndRepeatedObservationAreBounded(int remaining, CheckStatus expected, bool relative)
    {
        var fixture = await Fixture(relative ? Build("relative.pdf", "base") : Build("https://example.invalid/")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfExternalHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining); run.Observe(document, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationRetainsInitialUnknownOrKnownFailure(bool afterFailure, bool relative)
    {
        var fixture = await Fixture(relative ? Build("relative.pdf", "base") : Build("https://example.invalid/")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfExternalHyperlinkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
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
        var result = await Run(await Fixture(Build("https://example.invalid/"), limits: new() { MaxXmlFindings = limit }));
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
        var result = await Run(await Fixture(Build("https://example.invalid/"), missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialCoverageRetainsLocatedFailure(bool relative)
    {
        var fixture = await Fixture(relative ? Build("relative.pdf", "base") : Build("https://example.invalid/"));
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
        public string SourceId => "fda5205";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string uri, string scenario = "link", string? catalog = null)
    {
        var action = "<< /S /URI /URI (" + uri + ")" + (scenario == "wrong-type" ? " /Type /Bogus" : "") +
            (scenario == "next-action" ? " /Next << /S /URI /URI (https://example.invalid/other) >>" : "") + (scenario == "is-map" ? " /IsMap true" : "") + " >>";
        if (scenario == "bookmark") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (bookmark) /Parent 7 0 R /A " + action + " >>");
        if (scenario == "open-action") return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action);
        return PdfPigPackageInspectorTests.Build("/A " + action + (scenario == "conflict" ? " /Dest [3 0 R /Fit]" : "") +
            (scenario == "wrong-page" ? " /P 2 0 R" : "") + (scenario == "hidden" ? " /F 2" : ""), catalog ?? (scenario == "base" ? " /URI << /Base (https://base.invalid/) >>" : ""));
    }
}
