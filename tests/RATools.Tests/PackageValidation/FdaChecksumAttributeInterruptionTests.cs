using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaChecksumAttributeInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsAnExplicitResultAndKnownFailure(bool afterFailure)
    {
        var (target, context, lifecycle) = await Fixture();
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 2, cancellation.Token, check =>
        {
            retained = check;
            if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel();
        }).ToArray());
        Assert.NotNull(retained);
        Assert.Equal("FDA47-1408", retained.RuleId);
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained.Status);
        if (afterFailure) AssertFirstFailure(Assert.Single(retained.Findings));
        else Assert.Empty(retained.Findings);
    }

    [Fact]
    public async Task LaterFailuresDoNotMutateEarlierSnapshots()
    {
        var (target, context, lifecycle) = await Fixture();
        var snapshots = new List<RuleCheckResult>();
        var result = Assert.Single(FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 2, CancellationToken.None,
            check => { if (check.RuleId == "FDA47-1408" && check.Status == CheckStatus.Fail) snapshots.Add(check); }), check => check.RuleId == "FDA47-1408");
        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(2, snapshots.Count);
        AssertFirstFailure(Assert.Single(snapshots[0].Findings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileInspectorRetainsRuleResultAcrossItsDeadline(bool afterFailure)
    {
        var (inner, context, original) = await Fixture();
        var clock = new DeadlineClock();
        await using var target = new InterruptingCapture(inner, afterFailure, clock.Expire);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, original.XmlInspections, [], [], [], context.Catalog.Digest, context.Assets.Digest);
        target.Armed = true;
        var files = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.True(target.Triggered);
        Assert.Contains(files.Checks.SelectMany(item => item.Findings), item => item.Code == "FILE_INSPECTION_TIMEOUT");
        var check = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1408");
        var pending = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1425");
        Assert.Equal(CheckStatus.NotEvaluated, pending.Status);
        Assert.Empty(pending.Findings);
        var pendingDelete = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1426");
        Assert.Equal(CheckStatus.NotEvaluated, pendingDelete.Status);
        Assert.Empty(pendingDelete.Findings);
        var pendingIndexVersion = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1442");
        Assert.Equal(CheckStatus.NotEvaluated, pendingIndexVersion.Status);
        Assert.Empty(pendingIndexVersion.Findings);
        var pendingRegionalVersion = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1445");
        Assert.Equal(CheckStatus.NotEvaluated, pendingRegionalVersion.Status);
        Assert.Empty(pendingRegionalVersion.Findings);
        var pendingExtensions = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1476");
        Assert.Equal(CheckStatus.NotEvaluated, pendingExtensions.Status);
        Assert.Empty(pendingExtensions.Findings);
        var pendingTitles = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1478");
        Assert.Equal(CheckStatus.NotEvaluated, pendingTitles.Status);
        Assert.Empty(pendingTitles.Findings);
        var pendingTitleSpaces = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1482");
        Assert.Equal(CheckStatus.NotEvaluated, pendingTitleSpaces.Status);
        Assert.Empty(pendingTitleSpaces.Findings);
        var pendingPromotionalContact = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1545");
        Assert.Equal(CheckStatus.NotEvaluated, pendingPromotionalContact.Status);
        Assert.Empty(pendingPromotionalContact.Findings);
        var pendingMaterialIds = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1550");
        Assert.Equal(CheckStatus.NotEvaluated, pendingMaterialIds.Status);
        Assert.Empty(pendingMaterialIds.Findings);
        var pendingIssueDates = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1554");
        Assert.Equal(CheckStatus.NotEvaluated, pendingIssueDates.Status);
        Assert.Empty(pendingIssueDates.Findings);
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        if (afterFailure) AssertFirstFailure(Assert.Single(check.Findings));
        else Assert.Empty(check.Findings);
    }

    private static void AssertFirstFailure(ValidationFinding finding)
    {
        Assert.Equal("leaf-0", finding.Location.LeafId);
        Assert.Equal("@checksum-type", finding.Location.FieldPath);
        Assert.Equal("sha1", finding.Actual);
    }

    [Fact]
    public async Task TwoRulesShareOneFindingBudget()
    {
        var (target, context, lifecycle) = await Fixture();
        var checks = FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 3, CancellationToken.None).ToArray();
        Assert.Equal(3, checks.Sum(item => item.Findings.Count));
        Assert.Equal(2, Assert.Single(checks, item => item.RuleId == "FDA47-1408").Findings.Count);
        Assert.Single(Assert.Single(checks, item => item.RuleId == "FDA47-1425").Findings);
    }

    [Fact]
    public async Task StartingNextRuleDoesNotEraseEarlierFailure()
    {
        var (target, context, lifecycle) = await Fixture();
        using var cancellation = new CancellationTokenSource();
        var retained = new Dictionary<string, RuleCheckResult>();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 4, cancellation.Token, check =>
        {
            retained[check.RuleId] = check;
            if (check.RuleId == "FDA47-1425") cancellation.Cancel();
        }).ToArray());
        Assert.Equal(CheckStatus.Fail, retained["FDA47-1408"].Status);
        Assert.Equal(2, retained["FDA47-1408"].Findings.Count);
        Assert.Equal(CheckStatus.NotEvaluated, retained["FDA47-1425"].Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DefaultEvidenceDoesNotEstablishRequiredChecksum(bool defaultOperation, bool defaultChecksum)
    {
        var (target, context, lifecycle) = await Fixture(defaultOperation, defaultChecksum);
        var check = Assert.Single(FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 4, CancellationToken.None), item => item.RuleId == "FDA47-1425");
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(true, false, "abc")]
    [InlineData(false, true, "")]
    [InlineData(false, true, "abc")]
    public async Task DefaultDeleteEvidenceRemainsUnevaluated(bool defaultOperation, bool defaultChecksum, string checksum)
    {
        var (target, context, lifecycle) = await Fixture(defaultOperation, defaultChecksum, deleteOnly: true, checksumValue: checksum);
        var check = Assert.Single(FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 10, CancellationToken.None), item => item.RuleId == "FDA47-1426");
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(4, CheckStatus.NotEvaluated, 0)]
    [InlineData(5, CheckStatus.Fail, 1)]
    public async Task ThreeRulesShareTheBudgetInAuthorityOrder(int budget, CheckStatus expected, int deleteCount)
    {
        var (target, context, lifecycle) = await Fixture(mixedDelete: true);
        var checks = FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, budget, CancellationToken.None).ToArray();
        Assert.Equal(2, Assert.Single(checks, item => item.RuleId == "FDA47-1408").Findings.Count);
        Assert.Equal(2, Assert.Single(checks, item => item.RuleId == "FDA47-1425").Findings.Count);
        var check = Assert.Single(checks, item => item.RuleId == "FDA47-1426");
        Assert.Equal(expected, check.Status);
        Assert.Equal(deleteCount, check.Findings.Count);
        Assert.Equal(budget, checks.Sum(item => item.Findings.Count));
    }

    [Fact]
    public async Task StartingDeleteRuleRetainsEarlierFailures()
    {
        var (target, context, lifecycle) = await Fixture(mixedDelete: true);
        using var cancellation = new CancellationTokenSource();
        var retained = new Dictionary<string, RuleCheckResult>();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaLeafChecksumAttributeRules.Inspect(target, lifecycle, context, 10, cancellation.Token, check =>
        {
            retained[check.RuleId] = check;
            if (check.RuleId == "FDA47-1426") cancellation.Cancel();
        }).ToArray());
        foreach (var id in new[] { "FDA47-1408", "FDA47-1425" })
        {
            Assert.Equal(CheckStatus.Fail, retained[id].Status);
            Assert.Equal(2, retained[id].Findings.Count);
        }
        Assert.Equal(CheckStatus.NotEvaluated, retained["FDA47-1426"].Status);
        Assert.Empty(retained["FDA47-1426"].Findings);
    }

    [Fact]
    public async Task FileInspectorRetainsDeleteFailureAcrossDeadline()
    {
        var (inner, context, original) = await Fixture(mixedDelete: true);
        var clock = new DeadlineClock();
        await using var target = new InterruptingCapture(inner, true, clock.Expire, atFailureCall: 5);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, original.XmlInspections, [], [], [], context.Catalog.Digest, context.Assets.Digest);
        target.Armed = true;
        var files = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.True(target.Triggered);
        Assert.Contains(files.Checks.SelectMany(item => item.Findings), item => item.Code == "FILE_INSPECTION_TIMEOUT");
        foreach (var id in new[] { "FDA47-1408", "FDA47-1425" }) Assert.Equal(CheckStatus.Fail, Assert.Single(files.Checks, item => item.RuleId == id).Status);
        var check = Assert.Single(files.Checks, item => item.RuleId == "FDA47-1426");
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("leaf-2", finding.Location.LeafId);
        Assert.Equal("@checksum", finding.Location.FieldPath);
        Assert.Equal("abc", finding.Actual);
    }

    private static async Task<(ICapturedPackageInput Target, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle)> Fixture(
        bool defaultOperation = false, bool defaultChecksum = false, bool deleteOnly = false, bool mixedDelete = false, string checksumValue = "abc")
    {
        var context = PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new DateOnly(2026, 11, 19));
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var elements = new List<ParsedXmlElement>(); var leaves = new List<ParsedPackageLeaf>();
        for (var i = 0; i < (mixedDelete ? 4 : 2); i++)
        {
            var delete = deleteOnly || mixedDelete && i >= 2;
            var path = "/leaf[" + (i + 1) + "]";
            var location = new ValidationLocation("0001", "0001/index.xml", LeafId: "leaf-" + i, NodePath: path, Line: i + 1, Column: 1);
            var attributes = new List<ParsedXmlAttributeValue>
            {
                new("checksum-type", "checksum-type", "", delete ? "md5" : "sha1", false, i + 1, 2),
                new("operation", "operation", "", delete ? "delete" : "new", defaultOperation, i + 1, 3)
            };
            if (defaultChecksum || delete) attributes.Add(new("checksum", "checksum", "", checksumValue, defaultChecksum, i + 1, 4));
            elements.Add(new(path, null, "leaf", "leaf", "", attributes, [], "", location));
            leaves.Add(new(path, null, "leaf-" + i, null, "Title", delete ? "delete" : "new", null, null, delete ? checksumValue : null, delete ? "md5" : "sha1", null, null, true, false, location));
        }
        var backbone = new ParsedBackbone("0001/index.xml", PackageValidationCatalog.UsProfile, BackboneKind.Ich, true, CheckStatus.Pass,
            null, null, null, elements, [], leaves, [], []);
        var xml = new PackageXmlInspection(target.InputDigest, PackageValidationCatalog.UsProfile, context.Assets.Digest, target.Limits.Digest(), [backbone],
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", CheckStatus.Pass, "fixture")], context.Catalog.Digest);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, [xml], [], [], [], context.Catalog.Digest, context.Assets.Digest);
        return (target, context, lifecycle);
    }

    private sealed class InterruptingCapture(ICapturedPackageInput inner, bool afterFailure, Action interrupt, int atFailureCall = 1) : ICapturedPackageInput
    {
        private int _failureCalls;
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        private void Trigger(bool atFailure)
        {
            // Inject at an existing rule boundary, without a production-only test hook or timing race.
            if (Armed && !Triggered && afterFailure == atFailure && new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(FdaLeafChecksumAttributeRules), StringComparison.Ordinal) == true))
            {
                if (atFailure && ++_failureCalls != atFailureCall) return;
                Triggered = true; interrupt();
            }
        }
        public string SourceId { get { Trigger(true); return inner.SourceId; } }
        public string InputDigest { get { Trigger(false); return inner.InputDigest; } }
        public PackageInputManifest Manifest => inner.Manifest;
        public PackageReadLimits Limits => inner.Limits;
        public PackageSourceKind SourceKind => inner.SourceKind;
        public string? ArchiveSha256 => inner.ArchiveSha256;
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => inner.VerifyUnchangedAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; return new InertTimer(); }
        public void Expire() => _callback!(_state);
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
