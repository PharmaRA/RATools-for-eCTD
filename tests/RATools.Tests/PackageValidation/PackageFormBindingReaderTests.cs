using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

// Synthetic parsed facts isolate the binding budget from the upstream XML reader budget.
public sealed class PackageFormBindingReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TargetXmlMustMatchExactlyOnce(int copies)
    {
        var input = new Input(1000);
        var xml = Xml(input, [Backbone("0001/m1/us/us-regional.xml", 1, 1)]);
        var result = Read(input, Enumerable.Repeat(xml, copies));
        Assert.False(result.InventoryComplete);
        Assert.Empty(result.Groups);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WrongDigestOrProfileCannotProvideBindings(bool wrongDigest)
    {
        var input = new Input(1000);
        var xml = new PackageXmlInspection(wrongDigest ? "other-input" : input.InputDigest,
            wrongDigest ? PackageValidationCatalog.UsProfile : PackageValidationCatalog.EuProfile,
            PackageXmlAssets.Current.Digest, input.Limits.Digest(), [Backbone("0001/m1/us/us-regional.xml", 1, 1)], []);
        Assert.Empty(Read(input, [xml]).Groups);
    }

    [Fact]
    public void MultipleRegionalDocumentsRemainSeparateAndIncomplete()
    {
        var input = new Input(1000);
        var result = Read(input, [Xml(input, [Backbone("0001/m1/us/first.xml", 1, 1), Backbone("0001/m1/us/second.xml", 1, 1)])]);
        Assert.False(result.InventoryComplete);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(result.Groups[0].NodePath, result.Groups[1].NodePath);
        Assert.NotEqual(result.Groups[0].SourceLogicalPath, result.Groups[1].SourceLogicalPath);
    }

    [Theory]
    [InlineData(62, 2, false)]
    [InlineData(63, 3, true)]
    public void ExpandedBindingsRespectExactBudgetAndPublishOnlyWholeGroups(int budget, int expectedGroups, bool complete)
    {
        var input = new Input(budget);
        var backbone = Backbone("0001/m1/us/us-regional.xml", 10, 3);
        Assert.True(backbone.Elements.Count < budget); // 38 parsed elements; 3*(1+10+10)=63 binding observations.
        var result = Read(input, [Xml(input, [backbone])]);
        Assert.Equal(complete, result.InventoryComplete);
        Assert.Equal(expectedGroups, result.Groups.Count);
        Assert.All(result.Groups, group => Assert.Equal(10, group.Applications.Count));
    }

    [Fact]
    public void CancellationPropagatesBeforeAnyCandidatesArePublished()
    {
        var input = new Input(1000);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => Read(input, [Xml(input, [])], cancel.Token));
    }

    private static PackageFormBindings Read(Input input, IEnumerable<PackageXmlInspection> xml, CancellationToken cancellationToken = default)
    {
        var inputs = new PackageInputSet(input, new(input.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = new PackageLifecycleInspection(inputs, PackageValidationCatalog.UsProfile, xml, [], [], []);
        return PackageFormBindingReader.Read(input, lifecycle, cancellationToken);
    }

    private static PackageXmlInspection Xml(Input input, IEnumerable<ParsedBackbone> backbones) =>
        new(input.InputDigest, PackageValidationCatalog.UsProfile, PackageXmlAssets.Current.Digest,
            input.Limits.Digest(), backbones, []);

    private static ParsedBackbone Backbone(string path, int applications, int forms)
    {
        var elements = new List<ParsedXmlElement>();
        Add("/root", null, "fda-regional", "http://www.ich.org/fda");
        Add("/root/admin", "/root", "admin");
        Add("/root/admin/set", "/root/admin", "application-set");
        for (var index = 0; index < applications; index++)
        {
            var app = "/root/admin/set/a" + index;
            Add(app, "/root/admin/set", "application");
            Add(app + "/info", app, "application-information");
            Add(app + "/info/number", app + "/info", "application-number", text: index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
        }
        Add("/root/m1", "/root", "m1-regional");
        Add("/root/m1/forms", "/root/m1", "m1-1-forms");
        for (var index = 0; index < forms; index++) Add("/root/m1/forms/f" + index, "/root/m1/forms", "form");
        return new(path, PackageValidationCatalog.UsProfile, BackboneKind.UsRegional, true, CheckStatus.Pass,
            "fda-regional:fda-regional", null, null, elements, [], [], [], []);

        void Add(string node, string? parent, string name, string ns = "", string text = "") =>
            elements.Add(new(node, parent, name, name, ns, [], [], text, new("0001", path, NodePath: node)));
    }

    private sealed class Input(int budget) : ICapturedPackageInput
    {
        public string SourceId => "synthetic-binding-only";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new() { MaxXmlNodes = budget };
        public Stream OpenRead(string logicalPath) => throw new InvalidOperationException("Binding must not reopen content.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Binding reads parsed facts only.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
