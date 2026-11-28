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

public sealed class FdaPdfBookmarkPageRuleTests
{
    private const string RuleId = "FDA47-5102";

    [Theory]
    [InlineData("[1 /XYZ null null null]", false, false)]
    [InlineData("[1 /XYZ null null 0]", true, false)]
    [InlineData("[1 /XYZ null null null]", false, true)]
    [InlineData("[1 /XYZ 0 100 1.25]", false, false)]
    public async Task RemoteXyzIndexRequiresItsOwnPageExistenceQualification(string destination, bool targetFirst, bool history)
    {
        var fixture = await Fixture(destination, "valid", targetFirst, history);
        var result = await Run(fixture);
        var source = Assert.Single(result.Documents, item => item.LogicalPath == fixture.Source).Facts!;
        var target = Assert.Single(result.Documents, item => item.LogicalPath == fixture.Target).Facts!;
        Assert.Null(source.Basics.ParseError); Assert.Null(target.Basics.ParseError);
        Assert.Single(source.BookmarkFiles); Assert.Equal(1, target.QualifiedPageCount);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("zero-based-index=1", Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData("[0 /XYZ null null null]", "valid", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "valid", CheckStatus.Fail)]
    [InlineData("[2147483647 /XYZ null null 0]", "valid", CheckStatus.Fail)]
    [InlineData("[1 /XYZ null null null]", "missing", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "unselected-history", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "broken", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "count-mismatch", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "wrong-parent", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "wrong-literal", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "page-link", CheckStatus.NotEvaluated)]
    [InlineData("[-1 /XYZ null null null]", "valid", CheckStatus.NotEvaluated)]
    [InlineData("[1.5 /XYZ null null null]", "valid", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null -1]", "valid", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ (left) null 1]", "valid", CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null 1 0]", "valid", CheckStatus.NotEvaluated)]
    public async Task XyzPageFailureNeedsQualifiedIndexAndTarget(string destination, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(destination, scenario);
        var result = await Run(fixture);
        Assert.Null(Assert.Single(result.Documents, item => item.LogicalPath == fixture.Source).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status); Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(false, 0, CheckStatus.NotEvaluated)]
    [InlineData(false, 1, CheckStatus.Fail)]
    [InlineData(true, 0, CheckStatus.NotEvaluated)]
    [InlineData(true, 1, CheckStatus.Fail)]
    public async Task XyzBothObservationOrdersHonorAllowanceAndCancellation(bool targetFirst, int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("[1 /XYZ null null null]", "valid");
        var documents = (await Run(fixture)).Documents;
        var source = Assert.Single(documents, item => item.LogicalPath == fixture.Source);
        var target = Assert.Single(documents, item => item.LogicalPath == fixture.Target);
        var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, remaining); run.Observe(targetFirst ? source : target, remaining);
        run.Observe(source, 10); run.Observe(target, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        var cancelled = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
            result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); });
        cancelled.Observe(targetFirst ? target : source, 1);
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Observe(targetFirst ? source : target, 1));
        Assert.Equal(CheckStatus.Fail, retained!.Status); Assert.Single(retained.Findings);
    }

    [Theory]
    [InlineData("source-hash")]
    [InlineData("target-hash")]
    [InlineData("legacy-pages")]
    [InlineData("count-conflict")]
    public async Task XyzPageEvidenceCannotBorrowUnboundOrLegacyFacts(string variant)
    {
        var fixture = await Fixture("[1 /XYZ null null null]", "valid");
        var documents = (await Run(fixture)).Documents;
        var source = Assert.Single(documents, item => item.LogicalPath == fixture.Source);
        var target = Assert.Single(documents, item => item.LogicalPath == fixture.Target);
        if (variant == "source-hash") source = source with { CapturedSha256 = new string('0', 64) };
        if (variant == "target-hash") target = target with { CapturedSha256 = new string('0', 64) };
        if (variant is "legacy-pages" or "count-conflict") target = target with
        {
            Facts = new(target.Facts!.Basics, [], [], true, true, contentProcessing: PdfContentProcessingStatus.Processed,
                qualifiedPageCount: variant == "legacy-pages" ? null : 2)
        };
        var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(source, 10); run.Observe(target, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Fact]
    public async Task XyzPageFailureRetainsPartialReportCoverage()
    {
        var fixture = await Fixture("[1 /XYZ null null null]", "valid");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("[0 /Fit]", "valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "valid", false, false, CheckStatus.Fail)]
    [InlineData("[1 /Fit]", "valid", true, false, CheckStatus.Fail)]
    [InlineData("[1 /Fit]", "valid", false, true, CheckStatus.Fail)]
    [InlineData("[2147483647 /Fit]", "valid", false, false, CheckStatus.Fail)]
    [InlineData("[-1 /Fit]", "valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1.5 /Fit]", "valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /XYZ null null null]", "valid", false, false, CheckStatus.Fail)]
    [InlineData("(absent)", "valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("/absent", "valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "missing", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "unselected-history", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "broken", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "page-link", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "wrong-literal", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "count-mismatch", false, false, CheckStatus.NotEvaluated)]
    [InlineData("[1 /Fit]", "wrong-parent", false, false, CheckStatus.NotEvaluated)]
    public async Task RemotePageIndexRequiresCompleteBoundTargetPages(string destination, string scenario, bool targetFirst, bool history, CheckStatus expected)
    {
        var fixture = await Fixture(destination, scenario, targetFirst, history);
        var result = await Run(fixture);
        Assert.Null(Assert.Single(result.Documents, item => item.LogicalPath == fixture.Source).Facts!.Basics.ParseError);
        if (expected == CheckStatus.Fail)
            Assert.Null(Assert.Single(result.Documents, item => item.LogicalPath == fixture.Target).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal(fixture.Source, finding.Location.LogicalPath);
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
        var result = await Run(await Fixture("[1 /Fit]", "valid", date: date));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("valid", 1)]
    [InlineData("count", null)]
    [InlineData("parent", null)]
    [InlineData("duplicate", null)]
    [InlineData("cycle", null)]
    [InlineData("missing", null)]
    [InlineData("parsed", null)]
    [InlineData("budget", null)]
    [InlineData("io", null)]
    public void CompletePageTreeQualificationIsIndependent(string scenario, int? expected)
    {
        var root = new IndirectReferenceToken(new IndirectReference(2, 0));
        var leaf = new IndirectReferenceToken(new IndirectReference(3, 0));
        static DictionaryToken Dictionary(params (string Key, IToken Value)[] pairs) => new(pairs.ToDictionary(pair => NameToken.Create(pair.Key), pair => pair.Value));
        var catalog = Dictionary(("Pages", root));
        var parent = Dictionary(("Type", NameToken.Create("Pages")), ("Count", new NumericToken(scenario is "count" or "duplicate" ? 2 : 1)),
            ("Kids", new ArrayToken(scenario == "duplicate" ? [leaf, leaf] : scenario == "cycle" ? [root] : [leaf])));
        var page = Dictionary(("Type", NameToken.Create("Page")), ("Parent", scenario == "parent" ? leaf : root));
        IToken? Resolve(IToken? value) => value is not IndirectReferenceToken reference ? value : scenario == "io" ? throw new IOException("opaque") :
            reference.Data == root.Data ? parent : scenario == "missing" ? null : page;
        var reader = new PdfPigPackagePageTreeReader(Resolve, new() { MaxPdfObjects = scenario == "budget" ? 1 : 200 }, CancellationToken.None);
        Assert.Equal(expected, reader.Read(catalog, scenario == "parsed" ? 2 : 1));
    }

    [Theory]
    [InlineData("source-hash")]
    [InlineData("target-hash")]
    [InlineData("legacy-pages")]
    [InlineData("count-conflict")]
    [InlineData("zero-count")]
    [InlineData("target-parse")]
    [InlineData("source-no-bookmarks")]
    [InlineData("source-legacy")]
    [InlineData("no-index")]
    [InlineData("negative-index")]
    public async Task LegacyOrContradictoryFactsDoNotProvePageAbsence(string scenario)
    {
        var fixture = await Fixture("[1 /Fit]", "valid"); var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source); var target = documents.Single(item => item.LogicalPath == fixture.Target);
        if (scenario == "source-hash") source = source with { CapturedSha256 = new string('0', 64) };
        if (scenario == "target-hash") target = target with { CapturedSha256 = new string('0', 64) };
        if (scenario is "legacy-pages" or "count-conflict" or "zero-count" or "target-parse")
        {
            var facts = target.Facts!;
            target = target with { Facts = new(facts.Basics with { ParseError = scenario == "target-parse" ? "opaque" : null }, [], [], true, true,
                contentProcessing: PdfContentProcessingStatus.Processed, qualifiedPageCount: scenario switch { "legacy-pages" => null, "count-conflict" => 2, "zero-count" => 0, _ => 1 }) };
        }
        if (scenario is "source-no-bookmarks" or "source-legacy" or "no-index" or "negative-index")
        {
            var facts = source.Facts!;
            source = source with { Facts = new(facts.Basics with { HasBookmarks = scenario != "source-no-bookmarks" }, facts.Links, [], true, true,
                contentProcessing: scenario == "source-legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
                bookmarkFiles: [new("outline[1]/A/F", "b-target.pdf", scenario == "no-index" ? null : scenario == "negative-index" ? -1 : 1)]) };
        }
        var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(source, 10); run.Observe(target, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(false, 0, CheckStatus.NotEvaluated)]
    [InlineData(false, 1, CheckStatus.Fail)]
    [InlineData(true, 0, CheckStatus.NotEvaluated)]
    [InlineData(true, 1, CheckStatus.Fail)]
    public async Task EitherOrderHonorsAllowanceAndDoesNotRepeat(bool targetFirst, int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("[1 /Fit]", "valid"); var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source); var target = documents.Single(item => item.LogicalPath == fixture.Target);
        var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, remaining); run.Observe(targetFirst ? source : target, remaining);
        run.Observe(source, 10); run.Observe(target, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsInitialOrKnownResult(bool afterFailure)
    {
        var fixture = await Fixture("[1 /Fit]", "valid"); var documents = (await Run(fixture)).Documents;
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            foreach (var document in documents) run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(8, CheckStatus.NotEvaluated)]
    [InlineData(9, CheckStatus.NotEvaluated)]
    [InlineData(10, CheckStatus.Fail)]
    public async Task SharedBudgetPreservesOnlyAllocatedFindings(int limit, CheckStatus expected)
    {
        var result = await Run(await Fixture("[1 /Fit]", "valid", limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5117").Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task PartialReportRetainsPageFailureButDoesNotQualifyNamedDestinations()
    {
        var fixture = await Fixture("[1 /Fit]", "valid");
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task MissingArchivedSourceRemainsUnknown()
    {
        var result = await Run(await Fixture("[1 /Fit]", "valid", missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ConflictingSourcePageCannotAuthorizeOutOfRangeFinding(bool targetFirst, bool independent, bool history)
    {
        var fixture = await Fixture("[1 /Fit]", "valid", history: history);
        var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source);
        var target = documents.Single(item => item.LogicalPath == fixture.Target);
        Assert.Equal(1, target.Facts!.QualifiedPageCount);
        var original = Assert.Single(source.Facts!.BookmarkFiles);
        Assert.Equal(1L, original.ZeroBasedPageIndex);
        var bookmarks = new List<PdfBookmarkFileTarget> { original, original with { ZeroBasedPageIndex = 0 } };
        if (independent) bookmarks.Add(original with { ObjectPath = "outline[independent]/A/F" });
        source = source with { Facts = new(source.Facts.Basics, [], [], true, true,
            contentProcessing: PdfContentProcessingStatus.Processed, bookmarkFiles: bookmarks) };
        var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, 10);
        run.Observe(targetFirst ? source : target, 10);
        var check = run.Finish(false)!;
        Assert.Equal(independent ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(independent ? 1 : 0, check.Findings.Count);
        if (independent) Assert.Equal("outline[independent]/A/F; destination-page", Assert.Single(check.Findings).Location.FieldPath);
        foreach (var remaining in new[] { 0, 1 })
        {
            var bounded = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
            bounded.Observe(targetFirst ? target : source, remaining);
            bounded.Observe(targetFirst ? source : target, remaining);
            Assert.Equal(independent ? remaining : 0, bounded.FindingCount);
        }
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("duplicate", 0)]
    [InlineData("independent", 2)]
    public async Task DuplicateOrUnlocatedPageDestinationsRemainUnknown(string? variant, int expected)
    {
        var fixture = await Fixture("[1 /Fit]", "valid");
        var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source);
        var target = documents.Single(item => item.LogicalPath == fixture.Target);
        var original = Assert.Single(source.Facts!.BookmarkFiles);
        source = source with { Facts = new(source.Facts.Basics, [], [], true, true,
            contentProcessing: PdfContentProcessingStatus.Processed,
            bookmarkFiles: [original, original with { ObjectPath = variant == "duplicate" ? original.ObjectPath : variant! }]) };
        foreach (var targetFirst in new[] { false, true })
        {
            var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
            run.Observe(targetFirst ? target : source, 10);
            run.Observe(targetFirst ? source : target, 10);
            Assert.Equal(expected, run.FindingCount);
            Assert.Equal(expected > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompletePageDestinationScanCannotQueueOrPublish(bool targetFirst)
    {
        var baseline = await Fixture("[1 /Fit]", "valid");
        var documents = (await Run(baseline)).Documents;
        var source = documents.Single(item => item.LogicalPath == baseline.Source);
        var target = documents.Single(item => item.LogicalPath == baseline.Target);
        var original = Assert.Single(source.Facts!.BookmarkFiles);
        source = source with { Facts = new(source.Facts.Basics, [], [], true, true,
            contentProcessing: PdfContentProcessingStatus.Processed,
            bookmarkFiles: [original, original with { ObjectPath = "outline[2]/A/F" }, original with { ZeroBasedPageIndex = 0 }]) };
        var fixture = await Fixture("[1 /Fit]", "valid", limits: new PackageReadLimits { MaxPdfObjects = baseline.Input.Packages.Target.Manifest.Files.Count + 3 });
        Assert.Equal(source.CapturedSha256, fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == source.LogicalPath).Sha256);
        Assert.Equal(target.CapturedSha256, fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == target.LogicalPath).Sha256);
        var run = new FdaPdfBookmarkPageRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, 10);
        run.Observe(targetFirst ? source : target, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context, string Source, string Target);
    private static async Task<FixtureData> Fixture(string destination, string scenario, bool targetFirst = false, bool history = false, string? date = "2026-11-25", PackageReadLimits? limits = null, bool missingSource = false)
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
        var target = (history || scenario == "unselected-history" ? "0000/" : "0001/") + "b-target.pdf";
        var file = (history || scenario == "unselected-history" ? "../0000/" : "") + (scenario == "wrong-literal" ? "b%2Dtarget.pdf" : "b-target.pdf");
        var action = "/A << /S /GoToR /F (" + file + ") /D " + destination + " >>";
        var sourceBytes = scenario == "page-link" ? PdfPigPackageInspectorTests.Build(action) :
            PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R", "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (target) /Parent 7 0 R " + action + " >>");
        var targetBytes = scenario == "broken" ? "not a PDF"u8.ToArray() : Target(scenario);
        var additions = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [source] = sourceBytes };
        if (!history && scenario is not ("missing" or "unselected-history")) additions[target] = targetBytes;
        var current = new Capture(app, "0001", additions, readLimits);
        var old = history ? new Capture(app, "0000", new(StringComparer.Ordinal) { [target] = targetBytes }, readLimits) : null;
        var baseline = new HistoryBaselineManifest(app, "0001", old is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, old.SourceId, old.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
        var packages = new PackageInputSet(current, baseline, old is null ? [] : [old]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context, source, target);
    }

    private static byte[] Target(string scenario)
    {
        // Same byte lengths preserve xref offsets while changing exact structural facts.
        var text = System.Text.Encoding.ASCII.GetString(PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]"));
        if (scenario == "count-mismatch") text = text.Replace("/Count 1", "/Count 2", StringComparison.Ordinal);
        if (scenario == "wrong-parent") text = text.Replace("/Parent 2 0 R", "/Parent 1 0 R", StringComparison.Ordinal);
        return System.Text.Encoding.ASCII.GetBytes(text);
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
        public string SourceId => "fda5102-" + Manifest.SequenceNumber;
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
