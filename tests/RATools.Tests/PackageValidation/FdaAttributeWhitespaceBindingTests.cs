using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaAttributeWhitespaceBindingTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 26)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-leaf", CheckStatus.NotEvaluated)]
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("element-location", CheckStatus.NotEvaluated)]
    [InlineData("budget", CheckStatus.NotEvaluated)]
    [InlineData("budget-short", CheckStatus.NotEvaluated)]
    [InlineData("budget-exact", CheckStatus.Fail)]
    [InlineData("missing-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("missing-explicit", CheckStatus.NotEvaluated)]
    [InlineData("default", CheckStatus.Pass)]
    [InlineData("clean", CheckStatus.Pass)]
    [InlineData("stf", CheckStatus.Fail)]
    [InlineData("duplicate-stf", CheckStatus.NotEvaluated)]
    [InlineData("historical-stf", CheckStatus.NotEvaluated)]
    [InlineData("unknown-after-fail", CheckStatus.Fail)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("leaf-node-location", CheckStatus.NotEvaluated)]
    [InlineData("element-node-location", CheckStatus.NotEvaluated)]
    [InlineData("missing-leaf-element", CheckStatus.NotEvaluated)]
    [InlineData("cross-scope-duplicate", CheckStatus.NotEvaluated)]
    public async Task OnlyUniqueBoundCurrentAttributeEvidenceCanFail(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaAttributeWhitespaceRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Single(check.Findings);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceRemainsShared(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var check = Assert.Single(FdaAttributeWhitespaceRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status); Assert.True(check.Findings.Count <= remaining);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var fixture = await Fixture("valid");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaAttributeWhitespaceRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, cancellation.Token).ToArray());
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value; var input = new Input(new() { MaxXmlNodes = scenario switch { "budget" => 1, "budget-short" => 4, "budget-exact" => 5, _ => 500000 } });
        const string path = "0001/index.xml";
        var elements = new List<ParsedXmlElement>
        {
            new("/root", null, "ectd:ectd", "ectd", "http://www.ich.org/ectd",
                [new("xml:lang", "lang", "http://www.w3.org/XML/1998/namespace", "en", scenario == "default", 1, 1,
                    scenario == "missing-explicit" ? null : scenario == "clean" ? "en" : " en ")], [], "",
                new("0001", scenario == "element-location" ? "0000/index.xml" : path, NodePath: scenario == "element-node-location" ? "/different" : "/root"))
        };
        if (scenario == "duplicate-element") elements.Add(elements[0]);
        var leaf = new ParsedPackageLeaf("/root/leaf", "/root", "leaf", null, "Content", "new", null, "file.pdf", null, null, null, null, true, false,
            new("0001", path, LeafId: "leaf", NodePath: "/root/leaf"));
        var leaves = scenario == "duplicate-leaf" ? new[] { leaf, leaf } : Array.Empty<ParsedPackageLeaf>();
        if (scenario == "duplicate-leaf") elements.Add(new("/root/leaf", "/root", "leaf", "leaf", "", [], [], "", leaf.Location));
        if (scenario == "missing-leaf-element") leaves = [leaf];
        if (scenario is "leaf-location" or "leaf-node-location")
        {
            elements.Add(new("/root/leaf", "/root", "leaf", "leaf", "", [], [], "", leaf.Location));
            leaves = [leaf with { Location = leaf.Location with { LogicalPath = scenario == "leaf-location" ? "0000/index.xml" : path,
                NodePath = scenario == "leaf-node-location" ? "/different" : leaf.NodePath } }];
        }
        var backbone = new ParsedBackbone(scenario == "historical-document" ? "0000/index.xml" : path,
            scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : PackageValidationCatalog.UsProfile, BackboneKind.Ich, scenario != "unread", CheckStatus.Pass,
            "ectd:ectd", null, null, elements, [], leaves, [], []);
        var backbones = scenario == "duplicate-document" ? new[] { backbone, backbone } : new[] { backbone };
        var stfs = new List<ParsedStudyTaggingFile>();
        if (scenario is "stf" or "duplicate-stf" or "historical-stf")
        {
            backbones = [];
            stfs.Add(new(scenario == "historical-stf" ? "0000/index.xml" : path, null, true, CheckStatus.Pass, null, elements, [], []));
            if (scenario == "duplicate-stf") stfs.Add(stfs[0]);
        }
        if (scenario == "unknown-after-fail") stfs.Add(new("0001/unread-stf.xml", null, false, CheckStatus.NotEvaluated, null, [], [], []));
        if (scenario == "cross-scope-duplicate") stfs.Add(new(path, null, true, CheckStatus.Pass, null, elements, [], []));
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "different" : context.Assets.Digest,
            scenario == "wrong-limits" ? "different" : input.Limits.Digest(), backbones,
            [new("XML-OFFLINE", CheckStatus.Pass, "Complete"), new("XML-LIMITS", CheckStatus.Pass, "Complete")],
            scenario == "wrong-rules" ? "different" : context.Catalog.Digest, new(true, stfs));
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [],
            scenario == "wrong-lifecycle-rules" ? "different" : context.Catalog.Digest, scenario == "wrong-lifecycle-assets" ? "different" : context.Assets.Digest);
        return new(input, lifecycle, context);
    }

    private sealed class Input(PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => "fda1344-binding";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Attribute inspection must not reopen input files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
