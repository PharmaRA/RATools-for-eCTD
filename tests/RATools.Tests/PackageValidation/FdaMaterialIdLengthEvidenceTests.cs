using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaMaterialIdLengthEvidenceTests
{
    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task MultipleMaterialIdsShareBudgetAndSnapshots(int remaining, CheckStatus expected, int count)
    {
        var (target, context, lifecycle) = await Fixture("no-raw");
        var snapshots = new List<RuleCheckResult>();
        var check = FdaMaterialIdLengthRule.Inspect(target, lifecycle, context, remaining, CancellationToken.None,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count, snapshots.Count);
        if (count > 0) Assert.Single(snapshots[0].Findings);
        if (count == 2) Assert.Equal(2, check.Findings.Select(finding => finding.Location.NodePath).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("duplicate-node")]
    [InlineData("missing-parent")]
    [InlineData("broken-binding")]
    public async Task UnqualifiedAttributeOrAncestryFactsRemainUnevaluated(string scenario)
    {
        var (target, context, lifecycle) = await Fixture(scenario);
        var check = FdaMaterialIdLengthRule.Inspect(target, lifecycle, context, 2, CancellationToken.None);
        Assert.NotNull(check);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCancellationPropagatesAndPreservesProgress(bool afterFailure)
    {
        var (target, context, lifecycle) = await Fixture("no-raw");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaMaterialIdLengthRule.Inspect(target, lifecycle, context, 2, cancellation.Token, check =>
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
    public async Task FileInspectorRetainsPendingOrFirstFailureAcrossDeadline(bool afterFailure)
    {
        var (inner, context, original) = await Fixture("no-raw");
        var clock = new DeadlineClock();
        await using var target = new InterruptingCapture(inner, afterFailure, clock.Expire);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, original.XmlInspections, [], [], [], context.Catalog.Digest, context.Assets.Digest);
        target.Armed = true;
        var result = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.True(target.Triggered);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "FILE_INSPECTION_TIMEOUT");
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1550");
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(afterFailure ? 1 : 0, check.Findings.Count);
        if (afterFailure) Assert.Equal("@material-id", check.Findings[0].Location.FieldPath);
    }

    private static async Task<(ICapturedPackageInput Target, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle)> Fixture(string scenario)
    {
        var context = PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new DateOnly(2026, 11, 20));
        var regionalText = XDocument.Parse(Fda47AttributeRulesTests.Valid());
        regionalText.Root!.Element("m1-regional")!.Add(new XElement("m1-15-promotional-material", new XAttribute("promotional-material-audience-type", "fdapmat2"),
            new XElement("m1-15-2-materials", new XAttribute("promotional-material-doc-type", "fdapmdt1"),
                Enumerable.Range(0, 2).Select(_ => new XElement("m1-15-2-1-material", new XAttribute("promotional-material-type", "fdapmt1"), new XAttribute("material-id", new string('A', 31)))))));
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", regionalText.ToString(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var original = await new PackageXmlInspector(standards: context).InspectAsync(target, PackageValidationCatalog.UsProfile);
        var regional = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        var elements = regional.Elements.Where(element => scenario != "missing-parent" || element.Name != "m1-15-2-materials")
            .Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values.Select(attribute => attribute.Name == "material-id" ? attribute with { IsDefault = scenario == "default", ExplicitValue = null } : attribute),
                scenario == "broken-binding" && element.Name == "m1-15-2-materials" ? [] : element.Children, element.Text, element.Location)).ToList();
        if (scenario == "duplicate-node") elements.Add(elements.First(element => element.Name == "m1-15-2-1-material"));
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind, true, regional.DtdStatus,
            regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier, elements, [], [], [], regional.ResolvedAssets);
        var xml = new PackageXmlInspection(target.InputDigest, PackageValidationCatalog.UsProfile, context.Assets.Digest, target.Limits.Digest(),
            original.Backbones.Where(document => document.Kind != BackboneKind.UsRegional).Append(changed), original.Checks, context.Catalog.Digest, original.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, [xml], [], [], [], context.Catalog.Digest, context.Assets.Digest);
        return (target, context, lifecycle);
    }

    private sealed class InterruptingCapture(ICapturedPackageInput inner, bool afterFailure, Action interrupt) : ICapturedPackageInput
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        private void Trigger(bool atFailure)
        {
            if (Armed && !Triggered && afterFailure == atFailure && new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(FdaMaterialIdLengthRule), StringComparison.Ordinal) == true))
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
