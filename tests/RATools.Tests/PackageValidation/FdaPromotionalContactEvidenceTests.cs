using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPromotionalContactEvidenceTests
{
    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task RegionalDocumentsShareBudgetAndPreserveFailureSnapshots(int remaining, CheckStatus expected, int count)
    {
        var (target, context, lifecycle) = await Fixture("two-documents");
        var snapshots = new List<RuleCheckResult>();
        var check = FdaPromotionalContactRule.Inspect(target, lifecycle, context, remaining, CancellationToken.None,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count, snapshots.Count);
        if (count > 0) Assert.Single(snapshots[0].Findings);
        if (count == 2) Assert.Equal(2, check.Findings.Select(finding => finding.Location.LogicalPath).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("default-submission")]
    [InlineData("default-contact")]
    [InlineData("duplicate-document")]
    [InlineData("duplicate-inspection")]
    [InlineData("other-input")]
    public async Task DefaultOrAmbiguousFactsCannotAuthorizeAConclusion(string scenario)
    {
        var (target, context, lifecycle) = await Fixture(scenario);
        var check = FdaPromotionalContactRule.Inspect(target, lifecycle, context, 2, CancellationToken.None);
        Assert.NotNull(check);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCancellationPropagatesAndPreservesProgress(bool afterFailure)
    {
        var (target, context, lifecycle) = await Fixture("two-documents");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaPromotionalContactRule.Inspect(target, lifecycle, context, 2, cancellation.Token, check =>
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
    public async Task RealFileInspectorRetainsPendingOrFirstFailureAcrossDeadline(bool afterFailure)
    {
        var (inner, context, original) = await Fixture("two-documents");
        var clock = new DeadlineClock();
        await using var target = new InterruptingCapture(inner, afterFailure, clock.Expire);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, original.XmlInspections, [], [], [], context.Catalog.Digest, context.Assets.Digest);
        target.Armed = true;
        var result = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.True(target.Triggered);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "FILE_INSPECTION_TIMEOUT");
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1545");
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(afterFailure ? 1 : 0, check.Findings.Count);
        if (afterFailure) Assert.Equal("applicant-contacts", check.Findings[0].Location.FieldPath);
    }

    private static async Task<(ICapturedPackageInput Target, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle)> Fixture(string scenario)
    {
        var context = PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new DateOnly(2026, 11, 20));
        var regionalText = Fda47AttributeRulesTests.Valid().Replace("submission-type=\"fdast1\"", "submission-type=\"fdast8\"", StringComparison.Ordinal);
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", regionalText, PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var original = await new PackageXmlInspector(standards: context).InspectAsync(target, PackageValidationCatalog.UsProfile);
        var regional = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        var elements = regional.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
            element.Attributes.Values.Select(attribute => scenario == "default-submission" && attribute.Name == "submission-type" ||
                scenario == "default-contact" && attribute.Name == "applicant-contact-type" ? attribute with { IsDefault = true, ExplicitValue = null } : attribute),
            element.Children, element.Text, element.Location)).ToArray();
        ParsedBackbone Regional(string path) => new(path, regional.ProfileSnapshotId, regional.Kind, regional.ReadComplete, regional.DtdStatus,
            regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier,
            elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values, element.Children, element.Text, element.Location with { LogicalPath = path, BackboneRelativePath = path[5..] })), [], [], [], regional.ResolvedAssets);
        var documents = original.Backbones.Where(document => document.Kind != BackboneKind.UsRegional).Append(Regional(regional.LogicalPath)).ToList();
        if (scenario is "two-documents" or "duplicate-document") documents.Add(Regional(scenario == "two-documents" ? "0001/m1/us/z-other.xml" : regional.LogicalPath));
        var xml = new PackageXmlInspection(scenario == "other-input" ? new string('0', 64) : target.InputDigest, PackageValidationCatalog.UsProfile,
            context.Assets.Digest, target.Limits.Digest(), documents, original.Checks, context.Catalog.Digest, original.StudyTaggingFiles);
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
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(FdaPromotionalContactRule), StringComparison.Ordinal) == true))
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
