using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaExtraneousHeadingAttributeBindingTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 27)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("missing-capture", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("root-location", CheckStatus.NotEvaluated)]
    [InlineData("heading-location", CheckStatus.NotEvaluated)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("root-cycle", CheckStatus.NotEvaluated)]
    [InlineData("child-conflict", CheckStatus.NotEvaluated)]
    [InlineData("multiple-owners", CheckStatus.NotEvaluated)]
    [InlineData("missing-explicit", CheckStatus.NotEvaluated)]
    [InlineData("default", CheckStatus.Pass)]
    [InlineData("work", CheckStatus.NotEvaluated)]
    [InlineData("work-short", CheckStatus.NotEvaluated)]
    [InlineData("work-exact", CheckStatus.Fail)]
    [InlineData("unordered", CheckStatus.Fail)]
    [InlineData("known-before-unknown", CheckStatus.Fail)]
    [InlineData("unknown-before-known", CheckStatus.Fail)]
    public async Task ExtraneousHeadingRequiresBoundActualRoot(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaExtraneousHeadingAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceAndCancellationRemainBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var check = Assert.Single(FdaExtraneousHeadingAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status); Assert.Equal(remaining, check.Findings.Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaExtraneousHeadingAttributeRule.Inspect(fixture.Input,
            fixture.Lifecycle, fixture.Context, 10, cancellation.Token).ToArray());
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value;
        var input = new Input(scenario);
        var path = scenario == "historical-document" ? "0000/index.xml" : "0001/index.xml";
        var profile = context.Assets.ForProfile(PackageValidationCatalog.UsProfile).Single(item => item.Kind == BackboneKind.Ich);
        var elements = new List<ParsedXmlElement>
        {
            new("/root", null, "ectd:ectd", scenario == "wrong-root" ? "other" : profile.RootLocalName,
                scenario == "wrong-root-namespace" ? "urn:other" : profile.NamespaceUri,
                [], scenario == "child-conflict" ? [] : ["/root/m4"], "",
                new("0001", scenario == "root-location" ? "0001/other.xml" : path, NodePath: "/root")),
            new("/root/m4", scenario == "missing-parent" ? "/missing" : scenario == "cycle" ? "/root/m4" : "/root",
                "m4-nonclinical-study-reports", "m4-nonclinical-study-reports", "",
                [new("manufacturer", "manufacturer", "", "Acme", scenario == "default", 2, 1, scenario == "missing-explicit" ? null : "Acme")],
                scenario == "cycle" ? ["/root/m4"] : scenario == "root-cycle" ? ["/root"] : [], "",
                new("0001", scenario == "heading-location" ? "0001/other.xml" : path, NodePath: "/root/m4"))
        };
        if (scenario == "duplicate-element") elements.Add(elements[1]);
        if (scenario == "multiple-roots") elements.Add(new("/second", null, "ectd:ectd", "ectd", profile.NamespaceUri, [], [], "", new("0001", path, NodePath: "/second")));
        if (scenario == "multiple-owners") elements.Add(new("/root/other", "/root", "other", "other", "", [], ["/root/m4"], "", new("0001", path, NodePath: "/root/other")));
        var unknown = new ParsedXmlElement("/orphan", "/missing", "other", "other", "", [], [], "", new("0001", path, NodePath: "/orphan"));
        if (scenario == "known-before-unknown") elements.Add(unknown);
        if (scenario == "unknown-before-known") elements.Insert(0, unknown);
        if (scenario == "unordered") elements.Reverse();
        var backbone = new ParsedBackbone(path, scenario == "wrong-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile,
            BackboneKind.Ich, scenario != "unread", CheckStatus.Fail,
            profile.DocumentTypeName, null, null, elements, [], [], [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "wrong" : context.Assets.Digest,
            scenario == "wrong-limits" ? "wrong" : input.Limits.Digest(), scenario == "duplicate-document" ? [backbone, backbone] : [backbone],
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", CheckStatus.Pass, "fixture")],
            scenario == "wrong-rules" ? "wrong" : context.Catalog.Digest, new(true, []));
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile,
            scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [], scenario == "lifecycle-rules" ? "wrong" : context.Catalog.Digest,
            scenario == "lifecycle-assets" ? "wrong" : context.Assets.Digest);
        return new(input, lifecycle, context);
    }

    private sealed class Input : ICapturedPackageInput
    {
        public Input(string scenario)
        {
            Limits = new() { MaxXmlNodes = scenario switch { "work" => 1, "work-short" => 10, "work-exact" => 11, _ => 500000 } };
            Manifest = new(Guid.NewGuid(), "0001", scenario == "missing-capture" ? [] :
                [new("0001/index.xml", 1, new string('a', 64), new string('b', 32))], [], [], []);
        }
        public string SourceId => "fda1368-heading-binding";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => throw new InvalidOperationException("Do not reopen captured input.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
