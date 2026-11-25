using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaInvalidLeafPathEvidenceTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 26)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("default", CheckStatus.NotEvaluated)]
    [InlineData("no-explicit", CheckStatus.NotEvaluated)]
    [InlineData("normalized", CheckStatus.NotEvaluated)]
    [InlineData("wrong-attribute-namespace", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-href", CheckStatus.NotEvaluated)]
    [InlineData("missing-attribute", CheckStatus.NotEvaluated)]
    [InlineData("leaf-value-conflict", CheckStatus.NotEvaluated)]
    [InlineData("default-operation", CheckStatus.NotEvaluated)]
    [InlineData("delete", CheckStatus.NotEvaluated)]
    [InlineData("unknown-operation", CheckStatus.NotEvaluated)]
    [InlineData("operation-conflict", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("wrong-leaf-namespace", CheckStatus.NotEvaluated)]
    [InlineData("missing-leaf", CheckStatus.NotEvaluated)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-leaf", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("missing-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("budget", CheckStatus.NotEvaluated)]
    [InlineData("budget-short", CheckStatus.NotEvaluated)]
    [InlineData("budget-exact", CheckStatus.Fail)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("unknown-after-fail", CheckStatus.Fail)]
    public async Task OnlyBoundExplicitUnchangedLocalLeafEvidenceCanFail(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaInvalidLeafPathRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Single(check.Findings);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(-1, CheckStatus.NotEvaluated)]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingsCannotExceedTheRemainingAllowance(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var check = Assert.Single(FdaInvalidLeafPathRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        Assert.True(check.Findings.Count <= Math.Max(0, remaining));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var fixture = await Fixture("cycle");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaInvalidLeafPathRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, cancellation.Token).ToArray());
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowTheCurrentRule()
    {
        var fixture = await Fixture("valid"); var catalog = fixture.Context.Catalog;
        var old = new PackageStandardsContext(new ValidationRuleCatalog(catalog.Version, catalog.Digest, catalog.Sources.Values,
            catalog.Profiles.Values, catalog.Rules.Values.Where(item => item.InternalRuleId != "FDA47-1102")),
            fixture.Context.Assets, fixture.Context.NodeSchema, fixture.Context.SnapshotId, selectionDate: fixture.Context.SelectionDate);
        Assert.Empty(FdaInvalidLeafPathRule.Inspect(fixture.Input, fixture.Lifecycle, old, 10, CancellationToken.None));
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotBorrowUsRules()
    {
        var fixture = await Fixture("valid");
        var packages = new PackageInputSet(fixture.Input, new(fixture.Input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.IchProfile, [], [], [], [],
            fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.Empty(FdaInvalidLeafPathRule.Inspect(fixture.Input, lifecycle, fixture.Context, 10, CancellationToken.None));
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value;
        var input = new Input(new() { MaxXmlNodes = scenario switch { "budget" => 1, "budget-short" => 10, "budget-exact" => 11, _ => 500000 }, MaxXmlDepth = scenario == "depth" ? 1 : 128 });
        const string path = "0001/index.xml"; const string value = "m2/bad name.pdf";
        var profile = context.Assets.ForProfile(PackageValidationCatalog.UsProfile).Single(item => item.Kind == BackboneKind.Ich);
        var operation = scenario == "delete" ? "delete" : scenario == "unknown-operation" ? "other" : "new";
        var attributes = new List<ParsedXmlAttributeValue>
        {
            new("operation", "operation", "", operation, scenario == "default-operation", 2, 1, operation)
        };
        if (scenario != "missing-attribute") attributes.Add(new("xlink:href", "href", scenario == "wrong-attribute-namespace" ? "urn:other" : "http://www.w3c.org/1999/xlink",
            scenario == "normalized" ? "m2/good.pdf" : value, scenario == "default", 2, 20, scenario is "no-explicit" or "default" ? null : value));
        if (scenario == "duplicate-href") attributes.Add(attributes[1] with { Name = "link:href" });
        var elements = new List<ParsedXmlElement>
        {
            Element("/root", null, scenario == "wrong-root" ? "other" : profile.RootLocalName, scenario == "wrong-root-namespace" ? "urn:other" : profile.NamespaceUri),
            Element("/root/m2", scenario == "cycle" ? "/root/m2/leaf" : "/root", "m2-common-technical-document-summaries"),
            Element("/root/m2/leaf", scenario == "missing-parent" ? "/missing" : "/root/m2", "leaf", scenario == "wrong-leaf-namespace" ? "urn:other" : "", attributes)
        };
        if (scenario == "missing-leaf") elements.RemoveAt(2);
        if (scenario == "duplicate-element") elements.Add(elements[1]);
        if (scenario == "multiple-roots") elements.Add(Element("/other", null, profile.RootLocalName, profile.NamespaceUri));
        var leaf = new ParsedPackageLeaf("/root/m2/leaf", scenario == "missing-parent" ? "/missing" : "/root/m2", "leaf", null, "Content",
            scenario == "operation-conflict" ? "replace" : operation, null, scenario == "leaf-value-conflict" ? "other" : value, null, null, null, null, true, false,
            new("0001", scenario == "leaf-location" ? "0000/index.xml" : path, LeafId: "leaf", NodePath: "/root/m2/leaf"));
        var leaves = new List<ParsedPackageLeaf> { leaf };
        if (scenario == "duplicate-leaf") leaves.Add(leaf);
        if (scenario == "unknown-after-fail") leaves.Add(leaf with { NodePath = "/missing", Location = leaf.Location with { NodePath = "/missing" } });
        var backbone = new ParsedBackbone(scenario == "historical-document" ? "0000/index.xml" : path,
            scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : PackageValidationCatalog.UsProfile, BackboneKind.Ich, scenario != "unread", CheckStatus.Pass,
            profile.DocumentTypeName, null, null, elements, [], leaves, [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "different" : context.Assets.Digest,
            scenario == "wrong-limits" ? "different" : input.Limits.Digest(), scenario == "duplicate-document" ? [backbone, backbone] : [backbone], [],
            scenario == "wrong-rules" ? "different" : context.Catalog.Digest);
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [],
            scenario == "wrong-lifecycle-rules" ? "different" : context.Catalog.Digest, scenario == "wrong-lifecycle-assets" ? "different" : context.Assets.Digest);
        return new(input, lifecycle, context);

        static ParsedXmlElement Element(string node, string? parent, string name, string ns = "", IEnumerable<ParsedXmlAttributeValue>? attributes = null) =>
            new(node, parent, name, name, ns, attributes ?? [], [], "", new("0001", path, NodePath: node));
    }

    private sealed class Input(PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => "fda1102-evidence";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Rule must not reopen input files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
