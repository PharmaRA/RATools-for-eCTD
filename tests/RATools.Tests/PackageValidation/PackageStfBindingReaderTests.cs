using RATools.Application.PackageValidation;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using System.Security.Cryptography;
using System.Text;

namespace RATools.Tests.PackageValidation;

public sealed class PackageStfBindingReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualXmlReaderFactsResolveNestedReferencesWithoutRereading(bool deep)
    {
        var context = PackageStandardsContext.Current;
        await using var capture = new XmlCapture(deep);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var xml = await new PackageXmlInspector(standards: context).InspectAsync(capture, PackageValidationCatalog.UsProfile);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        Assert.True(index.ReadComplete);
        Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        Assert.True(stf.ReadComplete);
        Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, [xml], [], [], [], context.Catalog.Digest, context.Assets.Digest);
        capture.ForbidReads = true;
        var graph = PackageStfBindingReader.Read(packages, lifecycle, context, default);
        Assert.True(graph.Complete);
        var bound = Assert.Single(graph.Documents);
        Assert.True(Assert.Single(bound.Owners).Qualified);
        var reference = Assert.Single(bound.References);
        Assert.Equal(StfReferenceStatus.Resolved, reference.Status);
        Assert.Equal("content", reference.Target!.LeafId);
        Assert.Contains("/content-block[1]/doc-content[1]", reference.Element.NodePath, StringComparison.Ordinal);
        Assert.Equal("0001/index.xml", Assert.Single(bound.Owners).BackboneLogicalPath);
    }

    [Theory]
    [InlineData("valid", StfReferenceStatus.Resolved)]
    [InlineData("encoded", StfReferenceStatus.Resolved)]
    [InlineData("history-present", StfReferenceStatus.Resolved)]
    [InlineData("history-context", StfReferenceStatus.Resolved)]
    [InlineData("history-state-deleted", StfReferenceStatus.Resolved)]
    [InlineData("history-context-mismatch", StfReferenceStatus.BackboneUnavailable)]
    [InlineData("deep", StfReferenceStatus.Resolved)]
    [InlineData("history-absent", StfReferenceStatus.HistoryUnavailable)]
    [InlineData("backbone-unavailable", StfReferenceStatus.BackboneUnavailable)]
    [InlineData("backbone-incomplete", StfReferenceStatus.BackboneIncomplete)]
    [InlineData("backbone-invalid", StfReferenceStatus.BackboneInvalid)]
    [InlineData("leaf-missing", StfReferenceStatus.LeafMissing)]
    [InlineData("duplicate-id", StfReferenceStatus.AmbiguousId)]
    [InlineData("nonleaf", StfReferenceStatus.NotLeaf)]
    [InlineData("future", StfReferenceStatus.FutureReference)]
    [InlineData("external", StfReferenceStatus.InvalidReference)]
    [InlineData("query", StfReferenceStatus.InvalidReference)]
    [InlineData("no-fragment", StfReferenceStatus.InvalidReference)]
    [InlineData("empty-fragment", StfReferenceStatus.InvalidReference)]
    [InlineData("malformed", StfReferenceStatus.InvalidReference)]
    [InlineData("foreign-href", StfReferenceStatus.InvalidReference)]
    [InlineData("backbone-href-namespace", StfReferenceStatus.InvalidReference)]
    [InlineData("deleted-leaf", StfReferenceStatus.LeafUnqualified)]
    [InlineData("leaf-element-missing", StfReferenceStatus.LeafUnqualified)]
    [InlineData("leaf-default-operation", StfReferenceStatus.LeafUnqualified)]
    [InlineData("leaf-operation-mismatch", StfReferenceStatus.LeafUnqualified)]
    public void DistinguishesUnavailableHistoryUnreadBackbonesAndMissingLeaves(string scenario, object expected)
    {
        var fixture = Fixture(scenario);
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        var bound = Assert.Single(result.Documents, document => document.IsTarget);
        var reference = Assert.Single(bound.References);
        Assert.Equal(Assert.IsType<StfReferenceStatus>(expected), reference.Status);
        if (reference.Status == StfReferenceStatus.Resolved)
        {
            Assert.NotNull(reference.Target);
            Assert.Equal(scenario.StartsWith("history-", StringComparison.Ordinal) ? "0000" : "0001", reference.Target.SequenceNumber);
            Assert.Equal("content", reference.Target.LeafId);
            Assert.Equal(reference.Target, reference.Leaf!.Address);
        }
        else
        {
            Assert.Null(reference.Leaf);
            if (expected is StfReferenceStatus.InvalidReference) Assert.Null(reference.Target);
            else Assert.NotNull(reference.Target);
        }
        Assert.False(fixture.Target.Opened);
    }

    [Theory]
    [InlineData("valid", 1, true)]
    [InlineData("two-owners", 2, true)]
    [InlineData("unreferenced", 0, false)]
    [InlineData("owner-query", 1, false)]
    [InlineData("mixed-owner", 2, false)]
    [InlineData("owner-delete", 0, false)]
    public void PreservesAllActualOwnerCandidatesWithoutChoosingTheFirst(string scenario, int owners, bool qualified)
    {
        var fixture = Fixture(scenario);
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        var bound = Assert.Single(result.Documents);
        Assert.Equal(owners, bound.Owners.Count);
        Assert.Equal(qualified, bound.Complete);
        if (qualified) Assert.All(bound.Owners, owner => { Assert.True(owner.Qualified); Assert.Equal("m4-3-literature-references", owner.Heading); });
        else if (scenario == "mixed-owner") Assert.Single(bound.Owners, owner => owner.Qualified);
        else Assert.All(bound.Owners, owner => Assert.False(owner.Qualified));
    }

    [Theory]
    [InlineData("wrong-digest")]
    [InlineData("wrong-profile")]
    [InlineData("wrong-assets")]
    [InlineData("wrong-rules")]
    [InlineData("wrong-limits")]
    [InlineData("lifecycle-target")]
    [InlineData("lifecycle-baseline")]
    [InlineData("stf-hash-mismatch")]
    [InlineData("stf-not-captured")]
    [InlineData("duplicate-inspection")]
    public void UnboundOrAmbiguousObservationsAreNotAdopted(string scenario)
    {
        var fixture = Fixture(scenario);
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.False(result.Complete);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public void HistoryDocumentsRetainTheirOwnSourceAndAreNotMergedByStudyId()
    {
        var fixture = Fixture("history-stf");
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("stf-source-0001", Assert.Single(result.Documents, document => document.IsTarget).SourceId);
        Assert.Equal("stf-source-0000", Assert.Single(result.Documents, document => !document.IsTarget).SourceId);
        Assert.All(result.Documents, document => Assert.Single(document.Owners));
        Assert.All(result.Documents, document => Assert.Equal("same-study", Assert.Single(document.Document.Elements, element => element.Name == "study-id").Text));
    }

    [Fact]
    public void HistoricalStfRetainsAnActualTargetSequenceOwner()
    {
        var fixture = Fixture("cross-sequence-owner");
        var graph = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        var historical = Assert.Single(graph.Documents, document => !document.IsTarget);
        Assert.Equal(2, historical.Owners.Count);
        Assert.Contains(historical.Owners, owner => owner.BackboneLogicalPath == "0001/index.xml" && owner.Leaf.Id == "old-stf-reference");
        Assert.Contains(historical.Owners, owner => owner.BackboneLogicalPath == "0000/index.xml");
    }

    [Theory]
    [InlineData("foreign-doc-content")]
    [InlineData("outside-study-document")]
    [InlineData("broken-stf-binding")]
    public void ForeignDocContentAndUnboundDescendantsDoNotBecomeReferences(string scenario)
    {
        var fixture = Fixture(scenario);
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.Empty(Assert.Single(result.Documents).References);
    }

    [Fact]
    public void FutureReferenceIsRelativeToTheStfSequenceNotTheOverallTarget()
    {
        var fixture = Fixture("history-future");
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        var historical = Assert.Single(result.Documents, document => !document.IsTarget);
        var reference = Assert.Single(historical.References);
        Assert.Equal(StfReferenceStatus.FutureReference, reference.Status);
        Assert.Equal("0001", reference.Target!.SequenceNumber);
    }

    [Fact]
    public void ResultCollectionsAreFrozenCopies()
    {
        var fixture = Fixture("valid");
        var bound = Assert.Single(PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default).Documents);
        var owners = bound.Owners.ToList(); var references = bound.References.ToList();
        var copy = new BoundStudyTaggingFile(bound.SourceId, bound.InputDigest, true, bound.Document, true, owners, references);
        var documents = new List<BoundStudyTaggingFile> { copy };
        var graph = new PackageStfBindings(true, documents);
        owners.Clear(); references.Clear(); documents.Clear();
        Assert.Single(graph.Documents); Assert.Single(copy.Owners); Assert.Single(copy.References);
    }

    [Fact]
    public void CancellationDuringIndexingPropagates()
    {
        var fixture = Fixture("valid");
        using var cancellation = new CancellationTokenSource();
        var digestReads = 0;
        fixture.Target.OnDigestRead = () => { if (++digestReads == 2) cancellation.Cancel(); };
        Assert.ThrowsAny<OperationCanceledException>(() => PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, cancellation.Token));
        Assert.Equal(2, digestReads);
        Assert.False(fixture.Target.Opened);
    }

    [Fact]
    public void CancellationPropagatesBeforeAnyRead()
    {
        var fixture = Fixture("valid");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, cancellation.Token));
        Assert.False(fixture.Target.Opened);
    }

    [Fact]
    public void ExpandedAssociationsUseTheSelectedNodeBudget()
    {
        var fixture = Fixture("valid", new() { MaxXmlNodes = 1 });
        var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.False(result.Complete);
        Assert.True(result.Documents.Sum(document => document.Owners.Count + document.References.Count) <= 1);
    }

    [Fact]
    public void MultipleStfsShareOneBudgetRatherThanResettingIt()
    {
        var full = Fixture("two-stfs");
        var graph = PackageStfBindingReader.Read(full.Packages, full.Lifecycle, full.Context, default);
        Assert.True(graph.Complete);
        Assert.Equal(2, graph.Documents.Count);
        var partialFound = false;
        for (var maximum = 1; maximum <= 200; maximum++)
        {
            var fixture = Fixture("two-stfs", new() { MaxXmlNodes = maximum });
            var result = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
            if (result.Documents.Count == 1)
            {
                Assert.False(result.Complete);
                Assert.Single(result.Documents[0].References);
                partialFound = true;
                break;
            }
        }
        Assert.True(partialFound);
    }

    [Fact]
    public void ParentMembershipIndexConsumesTheSharedBudget()
    {
        var fixture = Fixture("wide-parent", new() { MaxXmlNodes = 100 });
        var graph = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.False(graph.Complete);
        Assert.Empty(graph.Documents);
    }

    private static (ObservationInput Target, PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context) Fixture(
        string scenario, PackageReadLimits? limits = null)
    {
        var context = PackageStandardsContext.Current;
        PackageStandardsContext? historyContext = null;
        if (scenario is "history-context" or "history-context-mismatch")
        {
            var catalog = context.Catalog;
            historyContext = new(new ValidationRuleCatalog(catalog.Version, new string('b', 64), catalog.Sources.Values, catalog.Profiles.Values, catalog.Rules.Values), context.Assets, context.NodeSchema);
            context = new(catalog, context.Assets, context.NodeSchema, history: new Dictionary<string, PackageStandardsContext> { ["0000"] = historyContext });
        }
        var app = Guid.NewGuid();
        var target = new ObservationInput(app, "0001", limits ?? new(), scenario);
        var history = scenario is "history-present" or "history-stf" or "history-context" or "history-context-mismatch" or "history-future" or "history-state-deleted" or "cross-sequence-owner"
            ? new ObservationInput(app, "0000", target.Limits) : null;
        var baseline = new HistoryBaselineManifest(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
        var packages = new PackageInputSet(target, baseline, history is null ? [] : [history]);
        var xml = Inspection(target, scenario, context);
        var inspections = new List<PackageXmlInspection> { xml };
        if (history is not null) inspections.Add(Inspection(history, scenario is "history-stf" or "cross-sequence-owner" ? "valid" : scenario == "history-future" ? "history-reference-future" : "history-content",
            scenario == "history-context-mismatch" ? context : historyContext ?? context));
        if (scenario == "duplicate-inspection") inspections.Add(xml);
        var lifecyclePackages = packages;
        if (scenario == "lifecycle-target")
        {
            var other = new ObservationInput(Guid.NewGuid(), "0001", target.Limits);
            lifecyclePackages = new(other, new(other.Manifest.ApplicationId, "0001", []), []);
        }
        if (scenario == "lifecycle-baseline")
        {
            var other = new ObservationInput(app, "0000", target.Limits);
            lifecyclePackages = new(target, new(app, "0001", [new("0000", HistorySourceKind.ImportedExternal, other.SourceId, other.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), [other]);
        }
        var lifecycle = new PackageLifecycleInspection(lifecyclePackages, PackageValidationCatalog.UsProfile, inspections, [],
            scenario == "history-state-deleted" ? [new(new(app, "0000", "index.xml", "content"), PackageLeafEffectiveness.Deleted, false)] : [], [], context.Catalog.Digest, context.Assets.Digest);
        return (target, packages, lifecycle, context);
    }

    private static PackageXmlInspection Inspection(ObservationInput input, string scenario, PackageStandardsContext context)
    {
        var sequence = input.Manifest.SequenceNumber;
        var path = sequence + "/index.xml";
        const string rootPath = "/ectd:ectd[1]";
        const string modulePath = rootPath + "/m4-nonclinical-study-reports[1]";
        const string headingPath = modulePath + "/m4-3-literature-references[1]";
        var leaves = new List<ParsedPackageLeaf>();
        var ids = new List<ParsedXmlId>();
        var elements = new List<ParsedXmlElement>
        {
            new(rootPath, null, "ectd:ectd", "ectd", "http://www.ich.org/ectd", [], [modulePath], "", new(sequence, path, NodePath: rootPath)),
            new(modulePath, rootPath, "m4-nonclinical-study-reports", "m4-nonclinical-study-reports", "", [], [headingPath], "", new(sequence, path, NodePath: modulePath))
        };
        void Leaf(string id, string href, string operation = "new")
        {
            var nodePath = headingPath + "/leaf[" + (leaves.Count + 1) + "]";
            var location = new ValidationLocation(sequence, path, "index.xml", id, nodePath, Line: 4, Column: 1);
            var attributes = new ParsedXmlAttributeValue[]
            {
                new("ID", "ID", "", id, false, 4, 2, id), new("operation", "operation", "", id == "content" && scenario == "leaf-operation-mismatch" ? "append" : operation,
                    id == "content" && scenario == "leaf-default-operation", 4, 3, operation),
                new("xlink:href", "href", "http://www.w3c.org/1999/xlink", href, false, 4, 4, href)
            };
            if (id != "content" || scenario != "leaf-element-missing") elements.Add(new(nodePath, headingPath, "leaf", "leaf", "", attributes, [], "", location));
            leaves.Add(new(nodePath, headingPath, id, new(input.Manifest.ApplicationId, sequence, "index.xml", id), "Content", operation, null,
                href, new string('0', 32), "md5", null, "context", true, false, location));
            ids.Add(new(id, nodePath, "leaf", true, location));
        }
        var stfRelative = scenario == "deep" ? "m4/study/deep/stf-a.xml" : "m4/study/stf-a.xml";
        if (scenario != "unreferenced") Leaf("stf", stfRelative + (scenario == "owner-query" ? "?unknown" : ""), scenario == "owner-delete" ? "delete" : "new");
        if (scenario == "two-owners") Leaf("stf-other", "m4/study/stf-a.xml");
        if (scenario == "mixed-owner") Leaf("stf-other", "m4/study/stf-a.xml?unknown");
        if (scenario == "two-stfs") Leaf("stf-second", "m4/study/stf-b.xml");
        if (scenario == "cross-sequence-owner") Leaf("old-stf-reference", "../0000/m4/study/stf-a.xml");
        if (scenario != "leaf-missing") Leaf("content", "m4/study/content.txt", scenario == "deleted-leaf" ? "delete" : "new");
        if (scenario == "duplicate-id") ids.Add(ids.Single(id => id.Id == "content"));
        if (scenario == "nonleaf") { ids.RemoveAll(id => id.Id == "content"); ids.Add(new("content", headingPath, "m4-3-literature-references", false, new(sequence, path, NodePath: headingPath))); }
        elements.Add(new(headingPath, modulePath, "m4-3-literature-references", "m4-3-literature-references", "", [],
            leaves.Select(leaf => leaf.NodePath).Concat(scenario == "wide-parent" ? Enumerable.Range(0, 1000).Select(index => headingPath + "/other[" + index + "]") : []),
            "", new(sequence, path, NodePath: headingPath)));
        var index = new ParsedBackbone(path, PackageValidationCatalog.UsProfile, BackboneKind.Ich, scenario != "backbone-incomplete",
            scenario == "backbone-invalid" ? CheckStatus.Fail : CheckStatus.Pass, "ectd:ectd", "util/dtd/ich-ectd-3-2.dtd", null, elements, [], leaves, ids, []);
        var stfPath = sequence + "/" + stfRelative;
        var href = scenario switch
        {
            "history-present" or "history-absent" or "history-context" or "history-context-mismatch" or "history-state-deleted" => "../../../0000/index.xml#content",
            "future" => "../../../0002/index.xml#content", "history-reference-future" => "../../../0001/index.xml#content", "deep" => "../../../index.xml#content",
            "backbone-unavailable" => "../../absent.xml#content", "external" => "https://example.invalid/index.xml#content",
            "query" => "../../index.xml?q#content", "no-fragment" => "../../index.xml", "empty-fragment" => "../../index.xml#",
            "malformed" => "../../index.xml#%ZZ", "encoded" => "../../index.xml#%63ontent", _ => "../../index.xml#content"
        };
        const string study = "/ectd:study[1]";
        const string documents = study + "/study-document[1]";
        const string referencePath = documents + "/doc-content[1]";
        const string identifier = study + "/study-identifier[1]";
        var stf = new ParsedStudyTaggingFile(stfPath, new string(scenario == "stf-hash-mismatch" ? 'b' : 'a', 64), true, CheckStatus.Pass, "../../util/dtd/ich-stf-v2-2.dtd",
            [new(study, null, "ectd:study", "study", "http://www.ich.org/ectd", [], [identifier, documents], "", new(sequence, stfPath, NodePath: study)),
                new(identifier, study, "study-identifier", "study-identifier", "", [], [identifier + "/study-id[1]"], "", new(sequence, stfPath, NodePath: identifier)),
                new(identifier + "/study-id[1]", identifier, "study-id", "study-id", "", [], [], "same-study", new(sequence, stfPath)),
                new(documents, study, "study-document", "study-document", "", [], scenario == "broken-stf-binding" ? [] : [referencePath], "", new(sequence, stfPath, NodePath: documents)),
                new(referencePath, scenario == "outside-study-document" ? identifier : documents, "doc-content", "doc-content", scenario == "foreign-doc-content" ? "urn:other" : "",
                    [new("xlink:href", "href", scenario == "foreign-href" ? "urn:other" : scenario == "backbone-href-namespace" ? "http://www.w3c.org/1999/xlink" : "http://www.w3.org/1999/xlink", href, false, 5, 3, href)], [], "", new(sequence, stfPath, NodePath: referencePath))], [], []);
        var stfs = new List<ParsedStudyTaggingFile>();
        if (scenario != "history-content") stfs.Add(stf);
        if (scenario == "two-stfs") stfs.Add(new(sequence + "/m4/study/stf-b.xml", stf.CapturedSha256, true, CheckStatus.Pass, stf.SystemIdentifier,
            stf.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values, element.Children, element.Text, element.Location with { LogicalPath = sequence + "/m4/study/stf-b.xml" })), [], []));
        return new(scenario == "wrong-digest" ? new string('0', 64) : input.InputDigest,
            scenario == "wrong-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile,
            scenario == "wrong-assets" ? new string('0', 64) : context.Assets.Digest, scenario == "wrong-limits" ? new string('0', 64) : input.Limits.Digest(), [index],
            [new("XML-OFFLINE", CheckStatus.Pass, "fixture"), new("XML-LIMITS", CheckStatus.Pass, "fixture")], scenario == "wrong-rules" ? new string('0', 64) : context.Catalog.Digest,
            new(true, stfs));
    }

    private sealed class XmlCapture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public XmlCapture(bool deep)
        {
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            {
                using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray();
            }, StringComparer.Ordinal);
            var stfPath = deep ? "m4/study/deep/stf-a.xml" : "m4/study/stf-a.xml";
            var relative = deep ? "../../../" : "../../";
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:ectd SYSTEM 'util/dtd/ich-ectd-3-2.dtd'>" +
                "<ectd:ectd xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3c.org/1999/xlink' dtd-version='3.2'>" +
                "<m1-administrative-information-and-prescribing-information><leaf ID='regional' operation='new' checksum='00000000000000000000000000000000' checksum-type='md5' xlink:href='m1/us/us-regional.xml'><title>Regional</title></leaf></m1-administrative-information-and-prescribing-information>" +
                "<m4-nonclinical-study-reports><m4-3-literature-references>" +
                "<leaf ID='stf' operation='new' checksum='00000000000000000000000000000000' checksum-type='md5' xlink:href='" + stfPath + "'><title>Study</title></leaf>" +
                "<leaf ID='content' operation='new' checksum='00000000000000000000000000000000' checksum-type='md5' xlink:href='m4/study/content.txt'><title>Content</title></leaf>" +
                "</m4-3-literature-references></m4-nonclinical-study-reports></ectd:ectd>");
            _files["0001/" + stfPath] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '" + relative + "util/dtd/ich-stf-v2-2.dtd'>" +
                "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>Study</title><study-id>A</study-id></study-identifier>" +
                "<study-document><content-block><block-title>Block</block-title><doc-content xlink:href='" + relative + "index.xml#content'><file-tag name='study-report-body' info-type='ich'/></doc-content></content-block></study-document></ectd:study>");
            _files["0001/m4/study/content.txt"] = "content"u8.ToArray();
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public bool ForbidReads { get; set; }
        public PackageInputManifest Manifest { get; }
        public string SourceId => "actual-xml-stf-binding";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => ForbidReads ? throw new InvalidOperationException("No rereading after inspection.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => ForbidReads ? throw new InvalidOperationException("Bindings use frozen facts.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ObservationInput(Guid applicationId, string sequence, PackageReadLimits limits, string scenario = "valid") : ICapturedPackageInput
    {
        public bool Opened { get; private set; }
        public Action? OnDigestRead { get; set; }
        public PackageInputManifest Manifest { get; } = new(applicationId, sequence,
            [new(sequence + "/index.xml", 1, new string('a', 64), new string('a', 32)),
                new(sequence + (scenario == "deep" ? "/m4/study/deep/stf-a.xml" : scenario == "stf-not-captured" ? "/m4/study/other.xml" : "/m4/study/stf-a.xml"), 1, new string('a', 64), new string('a', 32)),
                .. scenario == "two-stfs" ? new[] { new PackageInputFile(sequence + "/m4/study/stf-b.xml", 1, new string('a', 64), new string('a', 32)) } : [],
                new(sequence + "/m4/study/content.txt", 1, new string('a', 64), new string('a', 32))], [], [], []);
        public string SourceId => "stf-source-" + sequence;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest { get { OnDigestRead?.Invoke(); return Manifest.Digest; } }
        public PackageReadLimits Limits => limits;
        public Stream OpenRead(string logicalPath) { Opened = true; throw new InvalidOperationException("Bindings must use captured observations without rereading."); }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No input mutation expected.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
