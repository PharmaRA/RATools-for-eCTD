using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfDuplicateCategoryBindingTests
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
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("element-location", CheckStatus.NotEvaluated)]
    [InlineData("budget", CheckStatus.NotEvaluated)]
    [InlineData("budget-short", CheckStatus.NotEvaluated)]
    [InlineData("budget-exact", CheckStatus.Fail)]
    [InlineData("missing-xml", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-identifier", CheckStatus.NotEvaluated)]
    [InlineData("missing-identifier", CheckStatus.NotEvaluated)]
    [InlineData("element-node-location", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("dtd-failed", CheckStatus.NotEvaluated)]
    [InlineData("missing-name", CheckStatus.NotEvaluated)]
    [InlineData("name-namespace", CheckStatus.NotEvaluated)]
    [InlineData("realm-namespace", CheckStatus.NotEvaluated)]
    [InlineData("unknown-realm", CheckStatus.NotEvaluated)]
    [InlineData("unknown-after-fail", CheckStatus.Fail)]
    [InlineData("unread-after-fail", CheckStatus.Fail)]
    [InlineData("single", CheckStatus.Pass)]
    public async Task DuplicateCategoriesRequireUniqueCurrentBoundFacts(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaStfDuplicateCategoryRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
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
        var check = Assert.Single(FdaStfDuplicateCategoryRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        Assert.True(check.Findings.Count <= remaining);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var fixture = await Fixture("valid");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfDuplicateCategoryRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, cancellation.Token).ToArray());
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value;
        var input = new Input(new() { MaxXmlNodes = scenario switch { "budget" => 1, "budget-short" => 12, "budget-exact" => 13, _ => 500000 } });
        const string path = "0001/m4/study/stf.xml";
        var elements = new List<ParsedXmlElement>
        {
            Element("/study", null, scenario == "wrong-root" ? "other" : "study", scenario == "wrong-root-namespace" ? "urn:other" : "http://www.ich.org/ectd"),
            Element("/study/id", "/study", "study-identifier"),
            Element("/study/id/category1", "/study/id", "category", attributes: [new("name", "name", "", "species", false, 3, 1, "species"), new("info-type", "info-type", "", "ich", false, 3, 10, "ich")]),
            Element("/study/id/category2", "/study/id", "category", attributes: [new("name", "name", "", "species", false, 4, 1, "species"), new("info-type", "info-type", "", "ich", false, 4, 10, "ich")])
        };
        if (scenario == "duplicate-element") elements[3] = elements[2];
        if (scenario == "single") elements.RemoveAt(3);
        if (scenario == "missing-identifier") elements.RemoveAt(1);
        if (scenario == "duplicate-identifier") elements.Add(Element("/study/id2", "/study", "study-identifier"));
        if (scenario == "multiple-roots") elements.Add(Element("/other", null, "study", "http://www.ich.org/ectd"));
        if (scenario is "missing-name" or "name-namespace" or "realm-namespace" or "unknown-realm")
        {
            var attributes = elements[3].Attributes.Values.Select(attribute => attribute.Name switch
            {
                "name" when scenario == "name-namespace" => attribute with { NamespaceUri = "urn:other" },
                "info-type" when scenario == "realm-namespace" => attribute with { NamespaceUri = "urn:other" },
                "info-type" when scenario == "unknown-realm" => attribute with { Value = "eu" },
                _ => attribute
            }).Where(attribute => scenario != "missing-name" || attribute.Name != "name").ToArray();
            elements[3] = Element("/study/id/category2", "/study/id", "category", attributes: attributes);
        }
        if (scenario == "unknown-after-fail") elements.Add(Element("/study/id/category3", "/study/id", "category",
            attributes: [new("name", "name", "", "custom", false, 5, 1, "custom"), new("info-type", "info-type", "", "us", false, 5, 10, "us")]));
        var stf = new ParsedStudyTaggingFile(scenario == "historical-document" ? "0000/m4/study/stf.xml" : path, null, scenario != "unread",
            scenario == "dtd-failed" ? CheckStatus.Fail : CheckStatus.Pass, null, elements, [], []);
        var documents = new List<ParsedStudyTaggingFile> { stf };
        if (scenario == "duplicate-document") documents.Add(stf);
        if (scenario == "unread-after-fail") documents.Add(new("0001/m4/study/unread.xml", null, false, CheckStatus.NotEvaluated, null, [], [], []));
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "different" : context.Assets.Digest,
            scenario == "wrong-limits" ? "different" : input.Limits.Digest(), [], [], scenario == "wrong-rules" ? "different" : context.Catalog.Digest,
            new(true, documents));
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [],
            scenario == "wrong-lifecycle-rules" ? "different" : context.Catalog.Digest, scenario == "wrong-lifecycle-assets" ? "different" : context.Assets.Digest);
        return new(input, lifecycle, context);

        ParsedXmlElement Element(string node, string? parent, string name, string ns = "", ParsedXmlAttributeValue[]? attributes = null) =>
            new(node, parent, name, name, ns, attributes ?? [], [], "rat", new("0001", scenario == "element-location" ? "0000/m4/study/stf.xml" : path,
                NodePath: scenario == "element-node-location" ? "/different" : node));
    }

    private sealed class Input(PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => "fda1362-binding";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Duplicate-category inspection must not reopen files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
