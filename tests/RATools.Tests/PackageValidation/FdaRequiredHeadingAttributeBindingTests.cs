using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRequiredHeadingAttributeBindingTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 27)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("missing-capture", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("root-location", CheckStatus.NotEvaluated)]
    [InlineData("heading-location", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("root-cycle", CheckStatus.NotEvaluated)]
    [InlineData("child-conflict", CheckStatus.NotEvaluated)]
    [InlineData("multiple-owners", CheckStatus.NotEvaluated)]
    [InlineData("work", CheckStatus.NotEvaluated)]
    [InlineData("work-short", CheckStatus.NotEvaluated)]
    [InlineData("work-exact", CheckStatus.Fail)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("unordered", CheckStatus.Fail)]
    [InlineData("known-before-unknown", CheckStatus.Fail)]
    [InlineData("unknown-before-known", CheckStatus.Fail)]
    public async Task RequiredHeadingNeedsBoundRootedFacts(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaRequiredHeadingAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceAndCancellationRemainBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var check = Assert.Single(FdaRequiredHeadingAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status); Assert.Equal(remaining, check.Findings.Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaRequiredHeadingAttributeRule.Inspect(fixture.Input,
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
            Element("/root", null, scenario == "wrong-root" ? "other" : profile.RootLocalName,
                scenario == "wrong-root-namespace" ? "urn:other" : profile.NamespaceUri, ["/root/m2"]),
            Element("/root/m2", "/root", "m2-common-technical-document-summaries", "", ["/root/m2/clinical"]),
            Element("/root/m2/clinical", scenario == "missing-parent" ? "/missing" : scenario == "cycle" ? "/root/m2/clinical/efficacy" : "/root/m2",
                "m2-7-clinical-summary", "", scenario == "child-conflict" ? [] : ["/root/m2/clinical/efficacy"]),
            Element("/root/m2/clinical/efficacy", "/root/m2/clinical", "m2-7-3-summary-of-clinical-efficacy", "",
                scenario == "cycle" ? ["/root/m2/clinical"] : scenario == "root-cycle" ? ["/root"] : [])
        };
        if (scenario == "duplicate-element") elements.Add(elements[1]);
        if (scenario == "multiple-roots") elements.Add(Element("/second", null, profile.RootLocalName, profile.NamespaceUri, []));
        if (scenario == "multiple-owners") elements.Add(Element("/root/other", "/root", "other", "", ["/root/m2/clinical/efficacy"]));
        if (scenario == "known-before-unknown") elements.Add(Element("/orphan", "/missing", "unknown", "", []));
        if (scenario == "unknown-before-known") elements.Insert(0, Element("/orphan", "/missing", "unknown", "", []));
        if (scenario == "unordered") elements.Reverse();
        var backbone = new ParsedBackbone(path, scenario == "wrong-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile,
            BackboneKind.Ich, scenario != "unread", CheckStatus.Fail,
            profile.DocumentTypeName, null, null, elements, [], [], [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile,
            scenario == "wrong-assets" ? "wrong" : context.Assets.Digest, scenario == "wrong-limits" ? "wrong" : input.Limits.Digest(),
            scenario == "duplicate-document" ? [backbone, backbone] : [backbone],
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", CheckStatus.Pass, "fixture")],
            scenario == "wrong-rules" ? "wrong" : context.Catalog.Digest, new(true, []));
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile,
            scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [], scenario == "lifecycle-rules" ? "wrong" : context.Catalog.Digest,
            scenario == "lifecycle-assets" ? "wrong" : context.Assets.Digest);
        return new(input, lifecycle, context);

        ParsedXmlElement Element(string node, string? parent, string name, string ns, IEnumerable<string> children) =>
            new(node, parent, name, name, ns, [], children, "", new("0001",
                scenario == "root-location" && parent is null || scenario == "heading-location" && node.EndsWith("/efficacy", StringComparison.Ordinal)
                    ? "0001/other.xml" : path, NodePath: node));
    }

    private sealed class Input : ICapturedPackageInput
    {
        public Input(string scenario)
        {
            Limits = new() { MaxXmlNodes = scenario switch { "work" => 1, "work-short" => 20, "work-exact" => 21, _ => 500000 },
                MaxXmlDepth = scenario == "depth" ? 1 : 128 };
            Manifest = new(Guid.NewGuid(), "0001", scenario == "missing-capture" ? [] :
                [new("0001/index.xml", 1, new string('a', 64), new string('b', 32))], [], [], []);
        }
        public string SourceId => "fda1357-binding";
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
