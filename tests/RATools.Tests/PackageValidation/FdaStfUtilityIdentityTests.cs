using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfUtilityIdentityTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 27)));

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("unread-root", CheckStatus.Fail)]
    [InlineData("no-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-namespace", CheckStatus.NotEvaluated)]
    [InlineData("multiple-roots", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("root-location", CheckStatus.NotEvaluated)]
    [InlineData("root-node-location", CheckStatus.NotEvaluated)]
    [InlineData("hash", CheckStatus.NotEvaluated)]
    [InlineData("missing-capture", CheckStatus.NotEvaluated)]
    [InlineData("historical", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-xml", CheckStatus.NotEvaluated)]
    [InlineData("no-xml", CheckStatus.NotEvaluated)]
    [InlineData("xml-rules", CheckStatus.NotEvaluated)]
    [InlineData("xml-assets", CheckStatus.NotEvaluated)]
    [InlineData("xml-limits", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-rules", CheckStatus.NotEvaluated)]
    [InlineData("lifecycle-assets", CheckStatus.NotEvaluated)]
    [InlineData("work", CheckStatus.NotEvaluated)]
    [InlineData("known-before-unknown", CheckStatus.Fail)]
    [InlineData("unknown-before-known", CheckStatus.Fail)]
    public async Task ConditionalUtilitiesRequireActualBoundIdentity(string scenario, CheckStatus expected)
    {
        foreach (var checksum in new[] { false, true })
        {
            var fixture = await Fixture(scenario, checksum);
            var rule = fixture.Context.Catalog.Rules[checksum ? "FDA47-1130" : "FDA47-1119"];
            var check = FdaStfUtilityRules.Inspect(rule, fixture.Input, fixture.Lifecycle, fixture.Context, 10, CancellationToken.None);
            Assert.Equal(expected, check.Status);
            Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoAllowanceCannotPublishAndCancellationPropagates(bool checksum)
    {
        var fixture = await Fixture("valid", checksum);
        var rule = fixture.Context.Catalog.Rules[checksum ? "FDA47-1130" : "FDA47-1119"];
        var check = FdaStfUtilityRules.Inspect(rule, fixture.Input, fixture.Lifecycle, fixture.Context, 0, CancellationToken.None);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfUtilityRules.Inspect(rule,
            fixture.Input, fixture.Lifecycle, fixture.Context, 10, cancellation.Token));
    }

    private sealed record FixtureData(Input Input, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, bool checksum)
    {
        var context = await Context.Value;
        const string path = "0001/m4/study/stf-a.xml";
        var input = new Input(scenario, checksum);
        var documentPath = scenario == "historical" ? "0000/m4/study/stf-a.xml" : path;
        ParsedXmlElement Root(string node = "/ectd:study[1]") => new(node, null, "ectd:study",
            scenario == "wrong-root" ? "other" : "study", scenario == "wrong-namespace" ? "urn:other" : "http://www.ich.org/ectd",
            [], [], "", new("0001", scenario == "root-location" ? "0001/other.xml" : documentPath,
                NodePath: scenario == "root-node-location" ? "/other" : node));
        var root = Root();
        var elements = scenario == "no-root" ? Array.Empty<ParsedXmlElement>() : scenario == "multiple-roots" ? [root, Root("/other[1]")] :
            scenario == "duplicate-element" ? [root, root] : new[] { root };
        var document = new ParsedStudyTaggingFile(documentPath, new string(scenario == "hash" ? 'b' : 'a', 64),
            scenario != "unread-root", CheckStatus.Fail, "../../util/dtd/ich-stf-v2-2.dtd", elements, [], []);
        var unknown = new ParsedStudyTaggingFile("0001/m4/study/unknown.xml", new string('a', 64), false,
            CheckStatus.NotEvaluated, "../../util/dtd/ich-stf-v2-2.dtd", [], [], []);
        var documents = scenario switch
        {
            "duplicate-document" => new[] { document, document },
            "known-before-unknown" => [document, unknown],
            "unknown-before-known" => [unknown, document],
            _ => [document]
        };
        var xml = new PackageXmlInspection(input.InputDigest, PackageValidationCatalog.UsProfile,
            scenario == "xml-assets" ? "wrong" : context.Assets.Digest, scenario == "xml-limits" ? "wrong" : input.Limits.Digest(),
            [], [], scenario == "xml-rules" ? "wrong" : context.Catalog.Digest, new PackageStfInspection(false, documents));
        var packages = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile,
            scenario == "no-xml" ? [] : scenario == "duplicate-xml" ? [xml, xml] : [xml], [], [], [],
            scenario == "lifecycle-rules" ? "wrong" : context.Catalog.Digest, scenario == "lifecycle-assets" ? "wrong" : context.Assets.Digest);
        return new(input, lifecycle, context);
    }

    private sealed class Input : ICapturedPackageInput
    {
        public Input(string scenario, bool checksum)
        {
            Limits = new() { MaxXmlNodes = scenario == "work" ? 1 : 500000 };
            var files = new List<PackageInputFile>();
            if (scenario != "missing-capture") files.Add(new("0001/m4/study/stf-a.xml", 1, new string('a', 64), new string('0', 32)));
            files.Add(new("0001/m4/study/unknown.xml", 1, new string('a', 64), new string('0', 32)));
            if (checksum) files.Add(new("0001/util/dtd/ich-stf-v2-2.dtd", 1, new string('c', 64), new string('0', 32)));
            Manifest = new(Guid.NewGuid(), "0001", files, [], [], []);
        }
        public string SourceId => "stf-utility-identity";
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
