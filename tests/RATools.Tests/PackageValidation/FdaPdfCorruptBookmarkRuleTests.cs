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

public sealed class FdaPdfCorruptBookmarkRuleTests
{
    private const string RuleId = "FDA47-5101";

    [Theory]
    [InlineData("broken", false, false, CheckStatus.Fail)]
    [InlineData("broken", true, false, CheckStatus.Fail)]
    [InlineData("broken", false, true, CheckStatus.Fail)]
    [InlineData("valid", false, false, CheckStatus.NotEvaluated)]
    [InlineData("missing", false, false, CheckStatus.NotEvaluated)]
    [InlineData("unselected-history", false, false, CheckStatus.NotEvaluated)]
    [InlineData("page-link", false, false, CheckStatus.NotEvaluated)]
    [InlineData("open-action", false, false, CheckStatus.NotEvaluated)]
    [InlineData("uri", false, false, CheckStatus.NotEvaluated)]
    [InlineData("wrong-literal", false, false, CheckStatus.NotEvaluated)]
    [InlineData("literal-match", false, false, CheckStatus.Fail)]
    [InlineData("non-pdf-name", false, false, CheckStatus.NotEvaluated)]
    public async Task ExistingUnreadableTargetIsDistinctFromMissingOrUnqualifiedTarget(string scenario, bool targetFirst, bool history, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, targetFirst, history);
        var result = await Run(fixture);
        var source = Assert.Single(result.Documents, item => item.LogicalPath == fixture.Source);
        Assert.Null(source.Facts!.Basics.ParseError);
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
            Assert.Contains("future submissions", finding.RecommendedAction);
            Assert.DoesNotContain("Replace", finding.RecommendedAction);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5100").Status);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(PdfContentProcessingStatus.Unknown, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.Encrypted, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.InputUnavailable, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.ResourceLimited, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.InvalidContent, CheckStatus.Fail)]
    public async Task FailureMessageAloneDoesNotProveCorruptTarget(PdfContentProcessingStatus status, CheckStatus expected)
    {
        var fixture = await Fixture("broken");
        var result = await new PackagePdfValidator(new TargetInspector(fixture.Target, status), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task PolicyRequiresExplicitSelectionDate(string? date, CheckStatus expected)
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
    [InlineData("source-no-bookmarks")]
    [InlineData("source-legacy")]
    [InlineData("label-only")]
    public async Task BothSidesRequireIndependentConsistentCaptureEvidence(string scenario)
    {
        var fixture = await Fixture("broken"); var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source);
        var target = documents.Single(item => item.LogicalPath == fixture.Target);
        if (scenario == "source-hash") source = source with { CapturedSha256 = new string('0', 64) };
        if (scenario == "target-hash") target = target with { CapturedSha256 = new string('0', 64) };
        if (scenario.StartsWith("target-", StringComparison.Ordinal) && scenario != "target-hash")
        {
            var basics = target.Facts!.Basics with { PageCount = scenario == "target-pages" ? 1 : null,
                IsEncrypted = scenario == "target-encrypted", ParseError = scenario == "target-no-error" ? null : "opaque" };
            target = target with { Facts = new(basics, [], [], false, false, contentProcessing: PdfContentProcessingStatus.InvalidContent) };
        }
        if (scenario is "source-no-bookmarks" or "source-legacy" or "label-only")
        {
            var facts = source.Facts!;
            source = source with { Facts = new(facts.Basics with { HasBookmarks = scenario != "source-no-bookmarks" }, facts.Links, [], true, true,
                contentProcessing: scenario == "source-legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
                bookmarkFiles: scenario == "label-only" ? [] : facts.BookmarkFiles) };
        }
        var run = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        if (scenario != "missing-source") run.Observe(source, 10);
        if (scenario != "missing-target") run.Observe(target, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status); Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(false, 0, CheckStatus.NotEvaluated)]
    [InlineData(false, 1, CheckStatus.Fail)]
    [InlineData(true, 0, CheckStatus.NotEvaluated)]
    [InlineData(true, 1, CheckStatus.Fail)]
    public async Task EitherObservationOrderRespectsBudgetAndDoesNotRepeat(bool targetFirst, int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("broken"); var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source); var target = documents.Single(item => item.LogicalPath == fixture.Target);
        var run = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, remaining); run.Observe(targetFirst ? source : target, remaining);
        run.Observe(source, 10); run.Observe(target, 10);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("broken"); var documents = (await Run(fixture)).Documents;
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            foreach (var document in documents) run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(7, CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    [InlineData(8, CheckStatus.NotEvaluated, CheckStatus.Fail)]
    [InlineData(9, CheckStatus.Fail, CheckStatus.Fail)]
    public async Task SharedFindingAllowanceRetainsKnownFailureOnlyWhenAllocated(int limit, CheckStatus expected, CheckStatus contentStatus)
    {
        var result = await Run(await Fixture("broken", limits: new() { MaxXmlFindings = limit }));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(contentStatus, Assert.Single(result.Checks, item => item.RuleId == "FDA47-3102").Status);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == "FDA47-5117").Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task LaterLimitPreservesKnownCorruptBookmark()
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
    public async Task EmptyCurrentPdfScopeIsNotApplicable()
    {
        var result = await Run(await Fixture("no-pdf"));
        Assert.Equal(CheckStatus.NotApplicable, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task PartialCoverageRetainsLocatedFailureWithoutClaimingFullQualification()
    {
        var fixture = await Fixture("broken");
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        public string EngineVersion => "later-limit-bookmark-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken) =>
            logicalPath.EndsWith("z-later.pdf", StringComparison.Ordinal) ? throw new PdfInspectionLimitException("Later limit") :
                new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
    }

    private sealed class TargetInspector(string target, PdfContentProcessingStatus status) : IPackagePdfInspector
    {
        public string EngineVersion => "typed-bookmark-target-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken) =>
            logicalPath == target ? Failure(status) : new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
    }
    private static PackagePdfFacts Failure(PdfContentProcessingStatus status) => new(new PdfInspectionResult(null, false, null, false, null, [], false, [], ParseError: "opaque failure"), [], [], false, false, contentProcessing: status);
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
    public async Task ConflictingSourceBookmarkCannotAuthorizeCorruptTarget(bool targetFirst, bool independent, bool history)
    {
        var fixture = await Fixture("broken", history: history);
        var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source);
        var target = documents.Single(item => item.LogicalPath == fixture.Target);
        var original = Assert.Single(source.Facts!.BookmarkFiles);
        var bookmarks = new List<PdfBookmarkFileTarget> { original, original with { FileSpecification = "absent.pdf" } };
        if (independent) bookmarks.Add(original with { ObjectPath = "outline[independent]/A/F" });
        source = source with { Facts = new(source.Facts.Basics, [], [], true, true,
            contentProcessing: PdfContentProcessingStatus.Processed, bookmarkFiles: bookmarks) };
        var run = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, 10);
        run.Observe(targetFirst ? source : target, 10);
        var check = run.Finish(false)!;
        Assert.Equal(independent ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(independent ? 1 : 0, check.Findings.Count);
        if (independent) Assert.Equal("outline[independent]/A/F", Assert.Single(check.Findings).Location.FieldPath);
        foreach (var remaining in new[] { 0, 1 })
        {
            var bounded = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
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
    public async Task DuplicateAndUnlocatedSourceBookmarksDoNotAuthorizeExtraFindings(string? variant, int expected)
    {
        var fixture = await Fixture("broken");
        var documents = (await Run(fixture)).Documents;
        var source = documents.Single(item => item.LogicalPath == fixture.Source);
        var target = documents.Single(item => item.LogicalPath == fixture.Target);
        var original = Assert.Single(source.Facts!.BookmarkFiles);
        var second = original with { ObjectPath = variant == "duplicate" ? original.ObjectPath : variant! };
        source = source with { Facts = new(source.Facts.Basics, [], [], true, true,
            contentProcessing: PdfContentProcessingStatus.Processed, bookmarkFiles: [original, second]) };
        foreach (var targetFirst in new[] { false, true })
        {
            var run = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
            run.Observe(targetFirst ? target : source, 10);
            run.Observe(targetFirst ? source : target, 10);
            Assert.Equal(expected, run.FindingCount);
            Assert.Equal(expected > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteSourceUniquenessScanCannotQueueOrPublishFailure(bool targetFirst)
    {
        var baseline = await Fixture("broken");
        var documents = (await Run(baseline)).Documents;
        var source = documents.Single(item => item.LogicalPath == baseline.Source);
        var target = documents.Single(item => item.LogicalPath == baseline.Target);
        var original = Assert.Single(source.Facts!.BookmarkFiles);
        source = source with { Facts = new(source.Facts.Basics, [], [], true, true,
            contentProcessing: PdfContentProcessingStatus.Processed,
            bookmarkFiles: [original, original with { ObjectPath = "outline[2]/A/F" }, original with { FileSpecification = "absent.pdf" }]) };
        var fixture = await Fixture("broken", limits: new PackageReadLimits { MaxPdfObjects = baseline.Input.Packages.Target.Manifest.Files.Count + 3 });
        Assert.Equal(source.CapturedSha256, fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == source.LogicalPath).Sha256);
        Assert.Equal(target.CapturedSha256, fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == target.LogicalPath).Sha256);
        var run = new FdaPdfCorruptBookmarkRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(targetFirst ? target : source, 10);
        run.Observe(targetFirst ? source : target, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context, string Source, string Target);
    private static async Task<FixtureData> Fixture(string scenario, bool targetFirst = false, bool history = false, string? date = "2026-11-25", PackageReadLimits? limits = null, bool missingSource = false)
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
        var action = scenario == "uri" ? "/A << /S /URI /URI (" + specification + ") >>" : "/A << /S /GoToR /F (" + specification + ") /D [0 /Fit] >>";
        var sourceBytes = scenario == "page-link" ? PdfPigPackageInspectorTests.Build(action) : scenario == "open-action" ?
            PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/OpenAction " + action[3..]) :
            PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/Outlines 7 0 R", "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>", "<< /Title (target) /Parent 7 0 R " + action + " >>");
        byte[] targetBytes = scenario == "valid" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]") : "not a PDF"u8.ToArray();
        var currentFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (scenario != "no-pdf") currentFiles[source] = sourceBytes;
        if (!history && scenario is not ("missing" or "unselected-history" or "no-pdf")) currentFiles[target] = targetBytes;
        if (scenario == "later-limit") currentFiles["0001/z-later.pdf"] = sourceBytes;
        var current = new Capture(app, "0001", currentFiles, readLimits);
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
        public string SourceId => "fda5101-" + Manifest.SequenceNumber;
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
