using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaTextLengthBindingTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 26)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("keywords", CheckStatus.Fail)]
    [InlineData("default-keywords", CheckStatus.NotEvaluated)]
    [InlineData("missing-explicit", CheckStatus.NotEvaluated)]
    [InlineData("wrong-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-assets", CheckStatus.NotEvaluated)]
    [InlineData("wrong-limits", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("wrong-lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("missing-xml", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("historical-document", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("element-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("missing-parent", CheckStatus.NotEvaluated)]
    [InlineData("cycle", CheckStatus.NotEvaluated)]
    [InlineData("depth", CheckStatus.NotEvaluated)]
    [InlineData("budget", CheckStatus.NotEvaluated)]
    [InlineData("characters-short", CheckStatus.NotEvaluated)]
    [InlineData("characters-exact", CheckStatus.Fail)]
    [InlineData("unread-long", CheckStatus.Fail)]
    [InlineData("unread-short", CheckStatus.NotEvaluated)]
    [InlineData("title-children", CheckStatus.NotEvaluated)]
    [InlineData("hidden-title-child", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-title", CheckStatus.NotEvaluated)]
    [InlineData("unknown-after-fail", CheckStatus.Fail)]
    public async Task OnlyBoundUnambiguousOverlengthEvidenceCanFail(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(FdaTextLengthRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None));
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(512, 10, 0)]
    [InlineData(513, 10, 1)]
    [InlineData(1025, 10, 1)]
    [InlineData(1026, 10, 2)]
    [InlineData(1026, 1, 1)]
    [InlineData(1026, 0, 0)]
    public async Task TitleAndKeywordsShareCharacterAndFindingBudgets(long characters, int remaining, int count)
    {
        var fixture = await Fixture("both", characters);
        var check = Assert.Single(FdaTextLengthRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, remaining, CancellationToken.None));
        Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var fixture = await Fixture("valid");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaTextLengthRule.Inspect(fixture.Input, fixture.Lifecycle, fixture.Context, 10, cancellation.Token).ToArray());
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, long? characters = null)
    {
        var context = await Context.Value;
        var input = new Input(new() { MaxXmlNodes = scenario == "budget" ? 1 : 500000, MaxXmlDepth = scenario == "depth" ? 1 : 128,
            MaxXmlCharacters = characters ?? (scenario == "characters-short" ? 512 : scenario == "characters-exact" ? 513 : 16 * 1024 * 1024) });
        const string path = "0001/index.xml"; var longText = new string('A', 513);
        var isKeywords = scenario is "keywords" or "default-keywords" or "missing-explicit";
        var attributes = new List<ParsedXmlAttributeValue> { new("ID", "ID", "", "content", false, 3, 1, "content") };
        if (isKeywords || scenario == "both") attributes.Add(new("keywords", "keywords", "", longText, scenario == "default-keywords", 3, 20,
            scenario is "default-keywords" or "missing-explicit" ? null : longText));
        var elements = new List<ParsedXmlElement>
        {
            Element("/root", null, scenario == "wrong-root" ? "other" : "ectd", scenario == "wrong-root-namespace" ? "urn:other" : "http://www.ich.org/ectd"),
            Element("/root/m2", scenario == "cycle" ? "/root/m2/leaf" : "/root", "m2-common-technical-document-summaries"),
            Element("/root/m2/leaf", scenario == "missing-parent" ? "/missing" : "/root/m2", "leaf", attributes: attributes.ToArray()),
            Element("/root/m2/leaf/title", "/root/m2/leaf", "title", text: isKeywords ? "Short" : scenario == "unread-short" ? new string('A', 512) : longText,
                children: scenario == "title-children" ? ["/missing"] : [])
        };
        if (scenario == "duplicate-element") elements.Add(elements[3]);
        if (scenario == "multiple-roots") elements.Add(Element("/other", null, "ectd", "http://www.ich.org/ectd"));
        if (scenario == "hidden-title-child") elements.Add(Element("/root/m2/leaf/title/span", "/root/m2/leaf/title", "span"));
        if (scenario == "duplicate-title") elements.Add(Element("/root/m2/leaf/title2", "/root/m2/leaf", "title", text: longText));
        if (scenario == "unknown-after-fail") elements.Add(Element("/unknown", "/missing", "leaf"));
        var backbone = new ParsedBackbone(scenario == "historical-document" ? "0000/index.xml" : path,
            scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : PackageValidationCatalog.UsProfile, BackboneKind.Ich,
            scenario is not ("unread-long" or "unread-short"), CheckStatus.Pass, "ectd:ectd", null, null, elements, [], [], [], []);
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile, scenario == "wrong-assets" ? "different" : context.Assets.Digest,
            scenario == "wrong-limits" ? "different" : input.Limits.Digest(), scenario == "duplicate-document" ? [backbone, backbone] : [backbone], [],
            scenario == "wrong-rules" ? "different" : context.Catalog.Digest);
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [],
            scenario == "wrong-lifecycle-rules" ? "different" : context.Catalog.Digest, scenario == "wrong-lifecycle-assets" ? "different" : context.Assets.Digest);
        return new(input, lifecycle, context);
        ParsedXmlElement Element(string node, string? parent, string name, string ns = "", string text = "", ParsedXmlAttributeValue[]? attributes = null, string[]? children = null) =>
            new(node, parent, name, name, ns, attributes ?? [], children ?? [], text, new("0001", scenario == "element-location" ? "0000/index.xml" : path, NodePath: node));
    }

    private sealed class Input(PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => "fda1500-text";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Text length inspection must not reopen files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
