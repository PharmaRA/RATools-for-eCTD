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
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); }));
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

    private static async Task<(ICapturedPackageInput Target, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle)> Fixture()
    {
        var context = PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new DateOnly(2026, 11, 19));
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var elements = new List<ParsedXmlElement>(); var leaves = new List<ParsedPackageLeaf>();
        for (var i = 0; i < 2; i++)
        {
            var path = "/leaf[" + (i + 1) + "]";
            var location = new ValidationLocation("0001", "0001/index.xml", LeafId: "leaf-" + i, NodePath: path, Line: i + 1, Column: 1);
            elements.Add(new(path, null, "leaf", "leaf", "", [new("checksum-type", "checksum-type", "", "sha1", false, i + 1, 2)], [], "", location));
            leaves.Add(new(path, null, "leaf-" + i, null, "Title", "new", null, null, null, null, null, null, true, false, location));
        }
        var backbone = new ParsedBackbone("0001/index.xml", PackageValidationCatalog.UsProfile, BackboneKind.Ich, true, CheckStatus.Pass,
            null, null, null, elements, [], leaves, [], []);
        var xml = new PackageXmlInspection(target.InputDigest, PackageValidationCatalog.UsProfile, context.Assets.Digest, target.Limits.Digest(), [backbone],
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", CheckStatus.Pass, "fixture")], context.Catalog.Digest);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, [xml], [], [], [], context.Catalog.Digest, context.Assets.Digest);
        return (target, context, lifecycle);
    }

    private sealed class InterruptingCapture(ICapturedPackageInput inner, bool afterFailure, Action interrupt) : ICapturedPackageInput
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        private void Trigger(bool atFailure)
        {
            // Inject at an existing rule boundary, without a production-only test hook or timing race.
            if (Armed && !Triggered && afterFailure == atFailure && new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(FdaLeafChecksumAttributeRules), StringComparison.Ordinal) == true))
            { Triggered = true; interrupt(); }
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
