using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRegionalDtdVersionEvidenceTests
{
    [Theory]
    [InlineData("explicit-no-raw", CheckStatus.Pass)]
    [InlineData("absent", CheckStatus.Fail)]
    [InlineData("default", CheckStatus.Fail)]
    [InlineData("wrong-explicit", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-root", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-inspection", CheckStatus.NotEvaluated)]
    [InlineData("no-root", CheckStatus.NotEvaluated)]
    [InlineData("incomplete-selection", CheckStatus.NotEvaluated)]
    [InlineData("omitted-incomplete-selection", CheckStatus.Fail)]
    public async Task RootFactsAndSelectionCompletenessRemainSeparate(string scenario, CheckStatus expected)
    {
        var (target, context, lifecycle) = await Fixture(scenario);
        var check = FdaRegionalDtdVersionRule.Inspect(target, lifecycle, context, 2, CancellationToken.None);
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.All(check.Findings, AssertRootFinding);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task MultipleOmissionsShareTheExactRemainingBudget(int remaining, CheckStatus expected, int count)
    {
        var (target, context, lifecycle) = await Fixture("two-defaults");
        var snapshots = new List<RuleCheckResult>();
        var check = FdaRegionalDtdVersionRule.Inspect(target, lifecycle, context, remaining, CancellationToken.None,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count, snapshots.Count);
        if (count > 0)
        {
            Assert.Equal("0001/m1/us/us-regional.xml", check.Findings[0].Location.LogicalPath);
            Assert.Single(snapshots[0].Findings);
        }
        if (count == 2) Assert.Equal("0001/m1/us/z-other.xml", check.Findings[1].Location.LogicalPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalCancellationPreservesSnapshotButPropagates(bool afterFailure)
    {
        var (target, context, lifecycle) = await Fixture("two-defaults");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaRegionalDtdVersionRule.Inspect(target, lifecycle, context, 2, cancellation.Token, result =>
        {
            retained = result;
            if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel();
        }));
        Assert.NotNull(retained);
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained.Status);
        if (afterFailure) AssertRootFinding(Assert.Single(retained.Findings));
        else Assert.Empty(retained.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileInspectorRetainsPendingOrFirstFailureAcrossDeadline(bool afterFailure)
    {
        var (inner, context, original) = await Fixture("two-defaults");
        var clock = new DeadlineClock();
        await using var target = new InterruptingCapture(inner, afterFailure, clock.Expire);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, original.XmlInspections, [], [], [], context.Catalog.Digest, context.Assets.Digest);
        target.Armed = true;
        var result = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.True(target.Triggered);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "FILE_INSPECTION_TIMEOUT");
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1445");
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        if (afterFailure) AssertRootFinding(Assert.Single(check.Findings));
        else Assert.Empty(check.Findings);
    }

    private static void AssertRootFinding(ValidationFinding finding)
    {
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("/fda-regional:fda-regional[1]", finding.Location.NodePath);
        Assert.Equal("@dtd-version", finding.Location.FieldPath);
        Assert.Equal(2, finding.Location.Line);
        Assert.Null(finding.Location.LeafId);
    }

    private static async Task<(ICapturedPackageInput Target, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle)> Fixture(string scenario)
    {
        var context = PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new DateOnly(2026, 11, 20));
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var indexRoot = new ParsedXmlElement("/ectd:ectd[1]", null, "ectd:ectd", "ectd", "http://www.ich.org/ectd",
            [new("dtd-version", "dtd-version", "", "3.2", false, 2, 1)], [], "", new("0001", "0001/index.xml", NodePath: "/ectd:ectd[1]", Line: 2, Column: 1));
        var index = new ParsedBackbone("0001/index.xml", PackageValidationCatalog.UsProfile, BackboneKind.Ich, true, CheckStatus.Pass,
            "ectd:ectd", "util/dtd/ich-ectd-3-2.dtd", null, [indexRoot], [], [], [], []);
        var documents = new List<ParsedBackbone> { index };
        for (var i = 0; i < (scenario == "two-defaults" ? 2 : 1); i++)
        {
            var path = "0001/m1/us/" + (i == 0 ? "us-regional.xml" : "z-other.xml");
            var attributes = new List<ParsedXmlAttributeValue>();
            if (scenario != "absent") attributes.Add(new("dtd-version", "dtd-version", "", scenario == "wrong-explicit" ? "3.2" : "3.3",
                scenario is not ("explicit-no-raw" or "wrong-explicit" or "incomplete-selection"), 0, 0));
            var root = new ParsedXmlElement("/fda-regional:fda-regional[1]", null, "fda-regional:fda-regional", "fda-regional", "http://www.ich.org/fda",
                attributes, [], "", new("0001", path, NodePath: "/fda-regional:fda-regional[1]", Line: 2, Column: 1));
            var document = new ParsedBackbone(path, PackageValidationCatalog.UsProfile, BackboneKind.UsRegional, true, CheckStatus.Pass,
                "fda-regional:fda-regional", "../../util/dtd/us-regional-v3-3.dtd", null,
                scenario == "no-root" ? [] : scenario == "duplicate-root" ? [root, root] : [root], [], [], [], []);
            documents.Add(document);
            if (scenario == "duplicate-document") documents.Add(document);
        }
        var xml = new PackageXmlInspection(target.InputDigest, PackageValidationCatalog.UsProfile, context.Assets.Digest, target.Limits.Digest(), documents,
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", scenario.EndsWith("incomplete-selection", StringComparison.Ordinal) ? CheckStatus.NotEvaluated : CheckStatus.Pass, "fixture")], context.Catalog.Digest);
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
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(FdaRegionalDtdVersionRule), StringComparison.Ordinal) == true))
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
