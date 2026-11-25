using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaExtraneousContentAttributeBindingTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 26)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("default", CheckStatus.Pass)]
    [InlineData("missing-explicit", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("missing-xml", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("element-location", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("budget", CheckStatus.NotEvaluated)]
    [InlineData("budget-short", CheckStatus.NotEvaluated)]
    [InlineData("budget-exact", CheckStatus.Fail)]
    [InlineData("unknown-after-fail", CheckStatus.Fail)]
    public async Task ContentDefectsRequireUniqueBoundQualifiedAncestry(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var rule = fixture.Context.Catalog.Rules["FDA47-1368"];
        var check = FdaExtraneousContentAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, rule, 10, CancellationToken.None);
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceRemainsShared(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var check = FdaExtraneousContentAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context,
            fixture.Context.Catalog.Rules["FDA47-1368"], remaining, CancellationToken.None);
        Assert.Equal(expected, check.Status); Assert.True(check.Findings.Count <= remaining);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var fixture = await Fixture("cycle");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaExtraneousContentAttributeRule.Inspect(fixture.Input, fixture.Lifecycle,
            fixture.Context, fixture.Context.Catalog.Rules["FDA47-1368"], 10, cancellation.Token));
    }

    [Theory]
    [InlineData(0, 0, CheckStatus.NotEvaluated)]
    [InlineData(1, 1, CheckStatus.Fail)]
    [InlineData(2, 2, CheckStatus.Fail)]
    public async Task MultipleAttributesSpendOnlyTheRemainingAllowance(int remaining, int count, CheckStatus expected)
    {
        var fixture = await Fixture("two-defects");
        var check = FdaExtraneousContentAttributeRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context,
            fixture.Context.Catalog.Rules["FDA47-1368"], remaining, CancellationToken.None);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value;
        var input = new Input(new() { MaxXmlNodes = scenario switch { "budget" => 1, "budget-short" => 13, "budget-exact" => 14, _ => 500000 }, MaxXmlDepth = scenario == "depth" ? 2 : 128 });
        const string path = "0001/index.xml";
        var elements = new List<ParsedXmlElement>
        {
            Element("/root", null, scenario == "wrong-root" ? "other" : "ectd", "http://www.ich.org/ectd"),
            Element("/root/m2", "/root", "m2-common-technical-document-summaries"),
            Element("/root/m2/overview", "/root/m2", "m2-5-clinical-overview"),
            Element("/root/m2/overview/leaf", scenario == "missing-parent" ? "/missing" : scenario == "cycle" ? "/extension" : "/root/m2/overview", "leaf",
                attributes: [new("manufacturer", "manufacturer", "", "Acme", scenario == "default", 4, 10, scenario == "missing-explicit" ? null : "Acme")])
        };
        if (scenario == "two-defects") elements[3] = Element("/root/m2/overview/leaf", "/root/m2/overview", "leaf",
            attributes: [new("manufacturer", "manufacturer", "", "Acme", false, 4, 10, "Acme"), new("species", "species", "", "human", false, 4, 30, "human")]);
        if (scenario == "cycle") elements.Add(Element("/extension", "/extension", "node-extension"));
        if (scenario == "multiple-roots") elements.Add(Element("/other", null, "ectd", "http://www.ich.org/ectd"));
        if (scenario == "duplicate-element") elements.Add(elements[3]);
        if (scenario == "unknown-after-fail") elements.Add(Element("/unknown", "/missing", "leaf"));
        var backbone = new ParsedBackbone(scenario == "historical-document" ? "0000/index.xml" : path,
            scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : PackageValidationCatalog.UsProfile, BackboneKind.Ich, scenario != "unread", CheckStatus.Pass,
            "ectd:ectd", null, null, elements, [], [], [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "different" : context.Assets.Digest,
            scenario == "wrong-limits" ? "different" : input.Limits.Digest(), scenario == "duplicate-document" ? [backbone, backbone] : [backbone], [],
            scenario == "wrong-rules" ? "different" : context.Catalog.Digest);
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [], context.Catalog.Digest, context.Assets.Digest);
        return new(input, lifecycle, context);
        ParsedXmlElement Element(string node, string? parent, string name, string ns = "", ParsedXmlAttributeValue[]? attributes = null) =>
            new(node, parent, name, name, ns, attributes ?? [], [], "", new("0001", scenario == "element-location" ? "0000/index.xml" : path, NodePath: node));
    }

    private sealed class Input(PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => "fda1368-content";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Content attribute inspection must not reopen files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
