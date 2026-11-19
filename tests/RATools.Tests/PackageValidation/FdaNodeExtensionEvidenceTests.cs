using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaNodeExtensionEvidenceTests
{
    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task SharedBudgetAndFailureSnapshotsAreBounded(int remaining, CheckStatus expected, int count)
    {
        var (target, context, lifecycle) = await Fixture();
        var snapshots = new List<RuleCheckResult>();
        var check = FdaNodeExtensionRule.Inspect(target, lifecycle, context, remaining, CancellationToken.None,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count, snapshots.Count);
        if (count > 0) Assert.Single(snapshots[0].Findings);
        Assert.All(check.Findings, finding => { Assert.Equal(ValidationSeverity.Warning, finding.Severity); Assert.Null(finding.Location.LeafId); });
    }

    [Theory]
    [InlineData("duplicate-document")]
    [InlineData("duplicate-inspection")]
    [InlineData("other-input")]
    public async Task AmbiguousOrUnboundFactsDoNotBecomeFindings(string scenario)
    {
        var (target, context, lifecycle) = await Fixture(scenario);
        var check = FdaNodeExtensionRule.Inspect(target, lifecycle, context, 2, CancellationToken.None);
        Assert.NotNull(check);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCancellationPreservesProgressAndPropagates(bool afterFailure)
    {
        var (target, context, lifecycle) = await Fixture();
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaNodeExtensionRule.Inspect(target, lifecycle, context, 2, cancellation.Token, check =>
        {
            retained = check;
            if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel();
        }));
        Assert.NotNull(retained);
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained.Status);
        Assert.Equal(afterFailure ? 1 : 0, retained.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualFileInspectorRetainsPendingOrKnownExtensionAcrossDeadline(bool afterFailure)
    {
        var (inner, context, original) = await Fixture();
        var clock = new DeadlineClock();
        await using var target = new InterruptingCapture(inner, afterFailure, clock.Expire);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, original.XmlInspections, [], [], [], context.Catalog.Digest, context.Assets.Digest);
        target.Armed = true;
        var result = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.True(target.Triggered);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "FILE_INSPECTION_TIMEOUT");
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1476");
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(afterFailure ? 1 : 0, check.Findings.Count);
        if (afterFailure) Assert.Equal("/ectd:ectd[1]/node-extension[1]", check.Findings[0].Location.NodePath);
    }

    private static async Task<(ICapturedPackageInput Target, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle)> Fixture(string scenario = "valid")
    {
        var context = PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new DateOnly(2026, 11, 20));
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        const string rootPath = "/ectd:ectd[1]";
        var root = new ParsedXmlElement(rootPath, null, "ectd:ectd", "ectd", "http://www.ich.org/ectd",
            [new("dtd-version", "dtd-version", "", "3.2", false, 2, 1)], [rootPath + "/node-extension[1]", rootPath + "/node-extension[2]"], "",
            new("0001", "0001/index.xml", NodePath: rootPath, Line: 2, Column: 1));
        var elements = new List<ParsedXmlElement> { root };
        for (var i = 1; i <= 2; i++)
        {
            var path = rootPath + "/node-extension[" + i + "]";
            elements.Add(new(path, rootPath, "node-extension", "node-extension", "", [], [], "",
                new("0001", "0001/index.xml", NodePath: path, Line: i + 2, Column: 1)));
        }
        var document = new ParsedBackbone("0001/index.xml", PackageValidationCatalog.UsProfile, BackboneKind.Ich, true, CheckStatus.Fail,
            "ectd:ectd", "util/dtd/ich-ectd-3-2.dtd", null, elements, [], [], [], []);
        var xml = new PackageXmlInspection(scenario == "other-input" ? new string('0', 64) : target.InputDigest, PackageValidationCatalog.UsProfile,
            context.Assets.Digest, target.Limits.Digest(), scenario == "duplicate-document" ? [document, document] : [document],
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", CheckStatus.Pass, "fixture")], context.Catalog.Digest);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "duplicate-inspection" ? [xml, xml] : [xml],
            [], [], [], context.Catalog.Digest, context.Assets.Digest);
        return (target, context, lifecycle);
    }

    private sealed class InterruptingCapture(ICapturedPackageInput inner, bool afterFailure, Action interrupt) : ICapturedPackageInput
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        private void Trigger(bool atFailure)
        {
            if (Armed && !Triggered && afterFailure == atFailure && new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(FdaNodeExtensionRule), StringComparison.Ordinal) == true))
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
