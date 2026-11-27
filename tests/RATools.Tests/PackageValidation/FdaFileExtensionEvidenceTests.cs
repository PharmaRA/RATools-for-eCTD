using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFileExtensionEvidenceTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 27)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("missing-leaf", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-index", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-leaf", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-child", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("wrong-module-namespace", CheckStatus.NotEvaluated)]
    [InlineData("wrong-leaf-namespace", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("wrong-kind", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("dtd-fail", CheckStatus.NotEvaluated)]
    [InlineData("missing-capture", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("child-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-parent-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("root-location", CheckStatus.NotEvaluated)]
    [InlineData("default-operation", CheckStatus.NotEvaluated)]
    [InlineData("operation-conflict", CheckStatus.NotEvaluated)]
    [InlineData("default-href", CheckStatus.NotEvaluated)]
    [InlineData("no-explicit-href", CheckStatus.NotEvaluated)]
    [InlineData("normalized-href", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-href", CheckStatus.NotEvaluated)]
    [InlineData("href-namespace", CheckStatus.NotEvaluated)]
    [InlineData("id-conflict", CheckStatus.NotEvaluated)]
    [InlineData("work", CheckStatus.NotEvaluated)]
    [InlineData("work-short", CheckStatus.NotEvaluated)]
    [InlineData("work-exact", CheckStatus.Fail)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("unordered", CheckStatus.Fail)]
    [InlineData("known-before-unknown", CheckStatus.Fail)]
    [InlineData("unknown-before-known", CheckStatus.Fail)]
    [InlineData("conflicting-leaf-owner", CheckStatus.NotEvaluated)]
    [InlineData("conflicting-module-owner", CheckStatus.NotEvaluated)]
    public async Task ModuleAssignmentRequiresConsistentBoundBackbone(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaFileExtensionRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingsAndCancellationRemainBounded(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var check = Assert.Single(FdaFileExtensionRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status); Assert.Equal(remaining, check.Findings.Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaFileExtensionRule.Inspect(fixture.Input, fixture.Lifecycle,
            fixture.Context, 10, cancellation.Token).ToArray());
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value;
        var input = new Input(scenario);
        const string path = "0001/index.xml";
        const string module = "m2-common-technical-document-summaries";
        var profile = context.Assets.ForProfile(PackageValidationCatalog.UsProfile).Single(item => item.Kind == BackboneKind.Ich);
        var attributes = new List<ParsedXmlAttributeValue>
        {
            new("ID", "ID", "", scenario == "id-conflict" ? "other" : "leaf", false, 3, 1, "leaf"),
            new("operation", "operation", "", "new", scenario == "default-operation", 3, 1, "new"),
            new("xlink:href", "href", scenario == "href-namespace" ? "urn:other" : "http://www.w3c.org/1999/xlink",
                "same.txt", scenario == "default-href", 3, 1, scenario == "no-explicit-href" ? null : scenario == "normalized-href" ? "other.txt" : "same.txt")
        };
        if (scenario == "duplicate-href") attributes.Add(attributes[2] with { Name = "alias:href" });
        var elements = new List<ParsedXmlElement>
        {
            Element("/root", null, scenario == "wrong-root" ? "other" : profile.RootLocalName,
                scenario == "wrong-root-namespace" ? "urn:other" : profile.NamespaceUri, [], ["/root/m2"]),
            Element("/root/m2", scenario == "missing-parent" ? "/missing" : scenario == "cycle" ? "/root/m2/leaf" : "/root", module,
                scenario == "wrong-module-namespace" ? "urn:other" : "", [],
                scenario == "child-conflict" ? [] : scenario == "duplicate-child" ? ["/root/m2/leaf", "/root/m2/leaf"] : ["/root/m2/leaf"])
        };
        if (scenario != "missing-leaf") elements.Add(Element("/root/m2/leaf", "/root/m2", "leaf",
            scenario == "wrong-leaf-namespace" ? "urn:other" : "", attributes, scenario == "cycle" ? ["/root/m2"] : []));
        if (scenario == "duplicate-element") elements.Add(elements[1]);
        if (scenario == "multiple-roots") elements.Add(Element("/second", null, profile.RootLocalName, profile.NamespaceUri, [], []));
        if (scenario is "conflicting-leaf-owner" or "conflicting-module-owner")
            elements.Add(Element("/root/other", "/root", "other", "", [],
                [scenario == "conflicting-leaf-owner" ? "/root/m2/leaf" : "/root/m2"]));
        if (scenario == "unordered") elements.Reverse();
        var leaf = new ParsedPackageLeaf("/root/m2/leaf", scenario == "leaf-parent-conflict" ? "/wrong" : "/root/m2", "leaf", null,
            "Content", scenario == "operation-conflict" ? "replace" : "new", null, "same.txt", null, null, null, null, true, false,
            new("0001", scenario == "leaf-location" ? "0000/index.xml" : path, LeafId: "leaf", NodePath: "/root/m2/leaf"));
        var unknown = leaf with { NodePath = "/missing", Location = leaf.Location with { NodePath = "/missing" } };
        ParsedPackageLeaf[] leaves = scenario switch
        {
            "duplicate-leaf" => [leaf, leaf], "known-before-unknown" => [leaf, unknown],
            "unknown-before-known" => [unknown, leaf], _ => [leaf]
        };
        var backbone = new ParsedBackbone(path, scenario == "wrong-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile,
            scenario == "wrong-kind" ? BackboneKind.UsRegional : BackboneKind.Ich, scenario != "unread", scenario == "dtd-fail" ? CheckStatus.Fail : CheckStatus.Pass,
            profile.DocumentTypeName, null, null, elements, [], leaves, [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "wrong" : context.Assets.Digest,
            scenario == "wrong-limits" ? "wrong" : input.Limits.Digest(), scenario == "duplicate-index" ? [backbone, backbone] : [backbone], [],
            scenario == "wrong-rules" ? "wrong" : context.Catalog.Digest);
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile,
            scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [], scenario == "lifecycle-rules" ? "wrong" : context.Catalog.Digest,
            scenario == "lifecycle-assets" ? "wrong" : context.Assets.Digest);
        return new(input, lifecycle, context);

        ParsedXmlElement Element(string node, string? parent, string name, string ns,
            IEnumerable<ParsedXmlAttributeValue> attributes, IEnumerable<string> children) =>
            new(node, parent, name, name, ns, attributes, children, "",
                new("0001", scenario == "root-location" && parent is null ? "0000/index.xml" : path, NodePath: node));
    }

    private sealed class Input : ICapturedPackageInput
    {
        public Input(string scenario)
        {
            Limits = new() { MaxXmlNodes = scenario switch { "work" => 1, "work-short" => 19, "work-exact" => 20, _ => 500000 },
                MaxXmlDepth = scenario == "depth" ? 1 : 128 };
            var files = new List<PackageInputFile> { new("0001/same.txt", 1, new string('c', 64), new string('d', 32)) };
            if (scenario != "missing-capture") files.Add(new("0001/index.xml", 1, new string('a', 64), new string('b', 32)));
            Manifest = new(Guid.NewGuid(), "0001", files, [], [], []);
        }
        public string SourceId => "fda1255-evidence";
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
