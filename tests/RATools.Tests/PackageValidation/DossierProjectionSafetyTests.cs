using System.Reflection;
using RATools.Application.PackageValidation;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class DossierProjectionSafetyTests
{
    private static readonly Guid ApplicationId = Guid.NewGuid();

    [Fact]
    public void UnresolvedSourcesAndTargetsNeverBecomeEffectiveThroughEventOrdering()
    {
        var target = Leaf("target", "new", "m3/target.pdf");
        var source = Leaf("source", "append", "m3/source.pdf");
        var projection = Build([target, source],
            [new(target.Address!, PackageLeafEffectiveness.Unknown, false),
                new(source.Address!, PackageLeafEffectiveness.Unknown, false)],
            [new(source, target.Address, null, CheckStatus.NotEvaluated, []),
                new(target, null, null, CheckStatus.Pass, [])],
            [new("LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated, "Unresolved operation.")]);
        Assert.False(projection.IsComplete);
        Assert.Empty(projection.EffectiveLeaves);
        Assert.All(projection.Leaves, leaf => Assert.Equal(DossierLeafStatus.Unknown, leaf.Status));
    }

    [Fact]
    public void TerminalLifecycleLimitWithoutReplayCannotYieldACompleteEffectiveProjection()
    {
        var source = Leaf("source", "new", "m3/source.pdf");
        var projection = Build([source], [], [],
            [new("LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated, "LIFECYCLE_LIMIT")]);
        Assert.False(projection.IsComplete);
        Assert.NotEmpty(projection.IncompleteReasons);
        Assert.Empty(projection.EffectiveLeaves);
    }

    [Fact]
    public void IncompleteXmlCannotYieldACompleteProjection()
    {
        var source = Leaf("source", "new", "m3/source.pdf");
        var projection = Build([source], [new(source.Address!, PackageLeafEffectiveness.Current, false)],
            [new(source, null, null, CheckStatus.Pass, [])],
            [new("LIFECYCLE-EFFECTIVE", CheckStatus.Pass, "Resolved.")], readComplete: false);
        Assert.False(projection.IsComplete);
        Assert.NotEmpty(projection.IncompleteReasons);
    }

    [Theory]
    [InlineData("index.xml", "m3/source.pdf", "0002/m3/source.pdf")]
    [InlineData("m1/us/us-regional.xml", "cover.pdf", "0002/m1/us/cover.pdf")]
    [InlineData("m1/us/us-regional.xml", "../../../0001/m3/source.pdf", "0001/m3/source.pdf")]
    public void ContentPathsResolveHrefAgainstTheOwningBackbone(string backbone, string href, string expected)
    {
        var source = Leaf("source", "new", href, backbone);
        var projection = Build([source], [new(source.Address!, PackageLeafEffectiveness.Current, false)],
            [new(source, null, null, CheckStatus.Pass, [])],
            [new("LIFECYCLE-EFFECTIVE", CheckStatus.Pass, "Resolved.")], backbone: backbone);
        Assert.True(projection.IsComplete, string.Join(" | ", projection.IncompleteReasons));
        Assert.Equal(expected, Assert.Single(projection.EffectiveLeaves).ContentLogicalPath);
    }

    [Fact]
    public void VerifiedAppendMembershipIsKeptWithoutOverridingFinalUnknownState()
    {
        var target = Leaf("target", "new", "m3/target.pdf");
        var source = Leaf("source", "append", "m3/source.pdf");
        var projection = Build([target, source],
            [new(target.Address!, PackageLeafEffectiveness.Current, false),
                new(source.Address!, PackageLeafEffectiveness.Current, false)],
            [new(target, null, null, CheckStatus.Pass, []), new(source, target.Address, null, CheckStatus.Pass, [])],
            [new("LIFECYCLE-EFFECTIVE", CheckStatus.Pass, "Resolved.")]);
        Assert.True(projection.IsComplete);
        Assert.Equal(2, projection.EffectiveLeaves.Count);
        Assert.All(projection.EffectiveLeaves, leaf => Assert.Equal(DossierLeafStatus.AppendMember, leaf.Status));
    }

    private static ParsedPackageLeaf Leaf(string id, string operation, string href, string backbone = "index.xml") =>
        new("/leaf", null, id, new LeafAddress(ApplicationId, "0002", backbone, id), id, operation, null, href,
            null, null, null, null, true, false, new("0002", "0002/" + backbone));

    private static DossierProjection Build(ParsedPackageLeaf[] leaves, PackageLeafState[] states,
        PackageLifecycleEvent[] events, RuleCheckResult[] checks, bool readComplete = true, string backbone = "index.xml")
    {
        var capture = new Capture();
        var inputs = new PackageInputSet(capture, new HistoryBaselineManifest(ApplicationId, "0002", []), []);
        var document = Construct<ParsedBackbone>("0002/" + backbone, PackageValidationCatalog.UsProfile,
            default(BackboneKind), readComplete, CheckStatus.Pass, null, null, null,
            Array.Empty<ParsedXmlElement>(), Array.Empty<ParsedCtdNode>(), leaves, Array.Empty<ParsedXmlId>(),
            Array.Empty<string>(), null);
        var xml = Construct<PackageXmlInspection>(capture.InputDigest, PackageValidationCatalog.UsProfile,
            new string('a', 64), capture.Limits.Digest(), new[] { document }, Array.Empty<RuleCheckResult>(), null);
        var lifecycle = Construct<PackageLifecycleInspection>(inputs, PackageValidationCatalog.UsProfile,
            new[] { xml }, events, states, checks, null, null);
        return DossierProjectionBuilder.Build(lifecycle, new PackageDeliveryInput(inputs));
    }

    // Build immutable inspector facts without widening their production constructors or creating files.
    private static T Construct<T>(params object?[] arguments) where T : class =>
        (T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: arguments, culture: null)!;

    private sealed class Capture : ICapturedPackageInput
    {
        public string SourceId => "projection-memory";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(ApplicationId, "0002", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string logicalPath) => throw new NotSupportedException("Projection must not re-read content.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
