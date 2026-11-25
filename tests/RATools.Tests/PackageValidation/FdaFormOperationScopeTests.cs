using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFormOperationScopeTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 26)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("missing-leaf", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-xml-limits", CheckStatus.NotEvaluated)]
    [InlineData("wrong-xml-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-xml-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("missing-xml", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-backbone", CheckStatus.NotEvaluated)]
    [InlineData("wrong-document-profile", CheckStatus.NotEvaluated)]
    [InlineData("historical-path", CheckStatus.NotEvaluated)]
    [InlineData("leaf-namespace", CheckStatus.NotEvaluated)]
    [InlineData("parent-namespace", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("budget", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("unknown-after-fail", CheckStatus.Fail)]
    [InlineData("valid-empty", CheckStatus.Pass)]
    [InlineData("wrong-root-empty", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    public async Task ScopeMustBeRootedInUniqueBoundFdaXml(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var result = Assert.Single(FdaFormOperationRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail) Assert.Single(result.Findings);
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("new", false, 10, CheckStatus.Pass)]
    [InlineData("replace", false, 10, CheckStatus.NotEvaluated)]
    [InlineData("delete", false, 10, CheckStatus.Fail)]
    [InlineData("append", false, 0, CheckStatus.NotEvaluated)]
    [InlineData("new", true, 10, CheckStatus.NotEvaluated)]
    public async Task OperationAndAllowanceSemanticsArePreserved(string operation, bool isDefault, int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid", operation, isDefault);
        var result = Assert.Single(FdaFormOperationRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, result.Status);
        Assert.True(result.Findings.Count <= remaining);
        Assert.All(result.Findings, finding => Assert.Equal(ValidationSeverity.Info, finding.Severity));
    }

    [Fact]
    public async Task CancellationPropagatesWithoutTraversingMalformedParents()
    {
        var fixture = await Fixture("cycle");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaFormOperationRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, cancellation.Token).ToArray());
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string operation = "append", bool isDefault = false)
    {
        var context = await Context.Value; var input = new Input(new() { MaxXmlDepth = scenario == "depth" ? 1 : 128, MaxXmlNodes = scenario == "budget" ? 1 : 500000 });
        const string path = "0001/m1/us/us-regional.xml";
        var elements = new List<ParsedXmlElement>
        {
            Element("/root", null, scenario is "wrong-root" or "wrong-root-empty" ? "other" : "fda-regional", scenario == "wrong-root-namespace" ? "urn:other" : "http://www.ich.org/fda"),
            Element("/root/m1", scenario == "cycle" ? "/root/m1/cover" : "/root", "m1-regional", scenario == "parent-namespace" ? "urn:other" : ""),
            Element("/root/m1/cover", scenario == "missing-parent" ? "/missing" : "/root/m1", "m1-2-cover-letters"),
            Element("/root/m1/cover/leaf", "/root/m1/cover", "leaf", scenario == "leaf-namespace" ? "urn:other" : "",
                [new("operation", "operation", "", operation, isDefault, 1, 1, isDefault ? null : operation)])
        };
        if (scenario == "missing-leaf") elements.RemoveAt(3);
        if (scenario == "duplicate-element") elements.Add(elements[1]);
        if (scenario == "multiple-roots") elements.Add(Element("/other-root", null, "other"));
        var leaf = new ParsedPackageLeaf("/root/m1/cover/leaf", "/root/m1/cover", "form", null, "Form", operation, null, "form.pdf", null, null, null, null, true, false,
            new("0001", scenario == "leaf-location" ? "0001/index.xml" : path, LeafId: "form", NodePath: "/root/m1/cover/leaf"));
        var leaves = new List<ParsedPackageLeaf> { leaf };
        if (scenario is "valid-empty" or "wrong-root-empty") { leaves.Clear(); elements.RemoveAt(3); }
        if (scenario == "unknown-after-fail") leaves.Add(leaf with { NodePath = "/missing-leaf", Location = leaf.Location with { NodePath = "/missing-leaf" } });
        var backbone = new ParsedBackbone(scenario == "historical-path" ? "0000/m1/us/us-regional.xml" : path,
            scenario == "wrong-document-profile" ? PackageValidationCatalog.EuProfile : PackageValidationCatalog.UsProfile, BackboneKind.UsRegional, true, CheckStatus.Pass,
            "fda-regional:fda-regional", null, null, elements, [], leaves, [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-xml-assets" ? "different" : context.Assets.Digest,
            scenario == "wrong-xml-limits" ? "different" : input.Limits.Digest(), scenario == "duplicate-backbone" ? [backbone, backbone] : [backbone], [],
            scenario == "wrong-xml-rules" ? "different" : context.Catalog.Digest);
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [],
            scenario == "wrong-lifecycle-rules" ? "different" : context.Catalog.Digest, scenario == "wrong-lifecycle-assets" ? "different" : context.Assets.Digest);
        return new(input, lifecycle, context);

        static ParsedXmlElement Element(string node, string? parent, string name, string ns = "", ParsedXmlAttributeValue[]? attributes = null) =>
            new(node, parent, name, name, ns, attributes ?? [], [], "", new("0001", path, NodePath: node));
    }

    private sealed class Input(PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => "fda1038-scope";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Scope inspection must not reopen files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
