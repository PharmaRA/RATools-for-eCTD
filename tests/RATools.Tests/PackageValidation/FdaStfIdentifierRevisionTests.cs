using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfIdentifierRevisionTests
{
    [Theory]
    [InlineData("title", CheckStatus.Pass)]
    [InlineData("category", CheckStatus.Pass)]
    [InlineData("unchanged", CheckStatus.NotEvaluated)]
    [InlineData("missing-history", CheckStatus.NotEvaluated)]
    [InlineData("replace", CheckStatus.NotEvaluated)]
    public async Task EmptyRevisionRequiresActualIdentifierChangeAndBoundPredecessor(string scenario, CheckStatus expected)
    {
        var f = await Fixture(scenario);
        var graph = PackageStfBindingReader.Read(f.Packages, f.Lifecycle, f.Context, default);
        var current = Assert.Single(graph.Documents, d => d.IsTarget);
        Assert.True(current.StructureComplete);
        Assert.Empty(current.References);
        if (scenario != "missing-history")
        {
            var previous = Assert.Single(graph.Documents, d => !d.IsTarget);
            Assert.Equal(StfReferenceStatus.Resolved, Assert.Single(previous.References).Status);
            var operation = Assert.Single(f.Lifecycle.Events, e => e.Source.Address?.SequenceNumber == "0001" && e.Source.Id == "stf");
            Assert.Equal(CheckStatus.Pass, operation.Status);
            Assert.Equal(CheckStatus.Pass, operation.TargetLeafLookup);
            Assert.Equal("0000", operation.Target!.SequenceNumber);
        }
        var result = FdaStfLeafAssociationRule.Inspect(f.Packages, f.Lifecycle, f.Context, 10, default)!;
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("study-id-change")]
    [InlineData("category-removed")]
    [InlineData("category-order")]
    [InlineData("empty-block")]
    [InlineData("previous-empty")]
    [InlineData("previous-missing-leaf")]
    [InlineData("previous-tail")]
    [InlineData("missing-target")]
    [InlineData("multiple-owners")]
    [InlineData("competing-update")]
    [InlineData("content-deleted")]
    public async Task UnqualifiedRevisionContextStaysUnknown(string scenario)
    {
        var f = await Fixture(scenario);
        var result = FdaStfLeafAssociationRule.Inspect(f.Packages, f.Lifecycle, f.Context, 10, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("missing-raw-modified")]
    [InlineData("modified-conflict")]
    [InlineData("missing-raw-operation")]
    [InlineData("wrong-location")]
    [InlineData("wrong-profile")]
    [InlineData("event-target")]
    [InlineData("event-status")]
    [InlineData("duplicate-event")]
    [InlineData("previous-state")]
    [InlineData("duplicate-state")]
    [InlineData("old-rules")]
    [InlineData("old-hash")]
    [InlineData("duplicate-old-document")]
    [InlineData("duplicate-old-xml")]
    public async Task RevisionEvidenceMustAgreeAcrossFrozenConsumers(string scenario)
    {
        var f = await Fixture("title");
        var changedXml = f.Lifecycle.XmlInspections.Select(xml =>
        {
            var current = xml.InputDigest == f.Packages.Target.InputDigest;
            var backbones = xml.Backbones.Select(backbone =>
            {
                if (!current || backbone.Kind != BackboneKind.Ich) return backbone;
                var owner = Assert.Single(backbone.Leaves, l => l.Id == "stf");
                var elements = backbone.Elements.Select(e => new ParsedXmlElement(e.NodePath, e.ParentPath, e.Name, e.LocalName, e.NamespaceUri,
                    e.Attributes.Values.Select(a => e.NodePath != owner.NodePath ? a : (scenario, a.LocalName) switch
                    {
                        ("missing-raw-modified", "modified-file") => a with { ExplicitValue = null },
                        ("modified-conflict", "modified-file") => a with { ExplicitValue = "../0000/index.xml#other" },
                        ("missing-raw-operation", "operation") => a with { ExplicitValue = null }, _ => a
                    }), e.Children, e.Text, scenario == "wrong-location" && e.NodePath == owner.NodePath ? e.Location with { LogicalPath = "0000/index.xml" } : e.Location));
                return new ParsedBackbone(backbone.LogicalPath, scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : backbone.ProfileSnapshotId,
                    backbone.Kind, backbone.ReadComplete, backbone.DtdStatus, backbone.DocumentTypeName, backbone.SystemIdentifier, backbone.PublicIdentifier,
                    elements, backbone.Nodes, backbone.Leaves, backbone.IdIndex.Values.SelectMany(x => x), backbone.ResolvedAssets);
            });
            return new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, backbones, xml.Checks,
                !current && scenario == "old-rules" ? "different" : xml.RulesDigest,
                !current && scenario == "old-hash" ? new(xml.StudyTaggingFiles.InventoryComplete, xml.StudyTaggingFiles.Documents.Select(d =>
                    new ParsedStudyTaggingFile(d.LogicalPath, new string('f', 64), d.ReadComplete, d.DtdStatus, d.SystemIdentifier,
                        d.Elements, d.ProcessingInstructions, d.ResolvedAssets))) :
                !current && scenario == "duplicate-old-document" ? new(xml.StudyTaggingFiles.InventoryComplete,
                    xml.StudyTaggingFiles.Documents.Concat(xml.StudyTaggingFiles.Documents)) : xml.StudyTaggingFiles);
        }).ToList();
        if (scenario == "duplicate-old-xml") changedXml.Add(Assert.Single(changedXml, xml => xml.InputDigest != f.Packages.Target.InputDigest));
        var operation = Assert.Single(f.Lifecycle.Events, e => e.Source.Id == "stf" && e.Source.Address?.SequenceNumber == "0001");
        var events = f.Lifecycle.Events.Select(e => e != operation ? e : scenario switch
        {
            "event-target" => e with { TargetContentLogicalPath = "0000/m4/study/other.xml" },
            "event-status" => e with { Status = CheckStatus.NotEvaluated }, _ => e
        }).ToList();
        if (scenario == "duplicate-event") events.Add(operation);
        var states = f.Lifecycle.States.Select(s => scenario == "previous-state" && s.Address.SequenceNumber == "0000" && s.Address.LeafId == "stf" ?
            s with { Effectiveness = PackageLeafEffectiveness.Unknown } : s).ToList();
        if (scenario == "duplicate-state") states.Add(Assert.Single(states, s => s.Address.SequenceNumber == "0000" && s.Address.LeafId == "stf"));
        var lifecycle = new PackageLifecycleInspection(f.Packages, f.Lifecycle.ProfileSnapshotId, changedXml, events, states, f.Lifecycle.Checks,
            f.Lifecycle.RulesDigest, f.Lifecycle.AssetManifestDigest);
        var result = FdaStfLeafAssociationRule.Inspect(f.Packages, lifecycle, f.Context, 10, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task QualifiedRevisionDoesNotHideAnotherInitialEmptyStf()
    {
        var f = await Fixture("known-empty");
        var result = FdaStfLeafAssociationRule.Inspect(f.Packages, f.Lifecycle, f.Context, 1, default)!;
        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("0001/m4/study/other.xml", Assert.Single(result.Findings).Location.LogicalPath);
    }

    [Fact]
    public async Task ComponentExceptionCannotPromotePartialReportCoverage()
    {
        var f = await Fixture("title");
        foreach (var capture in f.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: f.Context)
            .InspectAsync(new(f.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Pass, Assert.Single(delivery.Files.Checks, c => c.RuleId == "FDA47-1816").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, c => c.RuleId == "FDA47-1816").CheckStatus);
    }

    [Fact]
    public async Task NoFindingBudgetIsNeededForAnEstablishedExceptionAndCancellationStillPropagates()
    {
        var f = await Fixture("title");
        Assert.Equal(CheckStatus.Pass, FdaStfLeafAssociationRule.Inspect(f.Packages, f.Lifecycle, f.Context, 0, default)!.Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfLeafAssociationRule.Inspect(f.Packages, f.Lifecycle, f.Context, 10, cancellation.Token));
    }

    [Theory]
    [InlineData("heading")]
    [InlineData("context")]
    public async Task PredecessorMustBelongToTheSameHeadingAndInstance(string scenario)
    {
        var f = await Fixture("title");
        var graph = PackageStfBindingReader.Read(f.Packages, f.Lifecycle, f.Context, default);
        var current = Assert.Single(graph.Documents, d => d.IsTarget);
        var previous = Assert.Single(graph.Documents, d => !d.IsTarget);
        Assert.True(FdaStfIdentifierRevision.Qualified(current, current.Owners, graph, f.Packages, f.Lifecycle, f.Context, () => { }));
        var owner = Assert.Single(previous.Owners);
        var changedOwner = scenario == "heading" ? owner with { Heading = "other-heading" } :
            owner with { Leaf = owner.Leaf with { ContextKey = "other-instance" } };
        var changed = new BoundStudyTaggingFile(previous.SourceId, previous.InputDigest, previous.IsTarget, previous.Document,
            previous.Complete, [changedOwner], previous.References, previous.StructureComplete);
        var altered = new PackageStfBindings(graph.Complete, [changed, current], graph.TargetScopeComplete);
        Assert.False(FdaStfIdentifierRevision.Qualified(current, current.Owners, altered, f.Packages, f.Lifecycle, f.Context, () => { }));
    }

    [Fact]
    public async Task QualificationConsumesItsSharedWorkBudgetAtTheExactBoundary()
    {
        var f = await Fixture("title");
        var graph = PackageStfBindingReader.Read(f.Packages, f.Lifecycle, f.Context, default);
        var current = Assert.Single(graph.Documents, d => d.IsTarget);
        var required = 0;
        Assert.True(FdaStfIdentifierRevision.Qualified(current, current.Owners, graph, f.Packages, f.Lifecycle, f.Context, () => required++));
        Assert.True(required > 0);
        var remaining = required - 1;
        Assert.Throws<WorkLimitException>(() => FdaStfIdentifierRevision.Qualified(current, current.Owners, graph, f.Packages, f.Lifecycle, f.Context, Tick));
        remaining = required;
        Assert.True(FdaStfIdentifierRevision.Qualified(current, current.Owners, graph, f.Packages, f.Lifecycle, f.Context, Tick));
        Assert.Equal(0, remaining);
        void Tick() { if (remaining-- <= 0) throw new WorkLimitException(); }
    }

    private sealed class WorkLimitException : Exception;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider())
            .CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new(2026, 11, 28));
        var app = Guid.NewGuid();
        var previous = new Capture(app, "0000", scenario);
        var current = new Capture(app, "0001", scenario);
        var packages = new PackageInputSet(current, new(app, "0001", scenario == "missing-history" ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, previous.SourceId, previous.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]),
            scenario == "missing-history" ? [] : [previous]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        Assert.All(lifecycle.XmlInspections.SelectMany(x => x.Backbones), d => Assert.Equal(CheckStatus.Pass, d.DtdStatus));
        foreach (var document in lifecycle.XmlInspections.SelectMany(x => x.StudyTaggingFiles.Documents))
            if (scenario == "previous-tail" && document.LogicalPath.StartsWith("0000/", StringComparison.Ordinal)) Assert.False(document.ReadComplete);
            else Assert.Equal(CheckStatus.Pass, document.DtdStatus);
        foreach (var input in packages.Sequences.Values.Cast<Capture>()) input.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string scenario)
        {
            var current = sequence == "0001";
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var seed = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = seed.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var input = seed.OpenRead(file.LogicalPath); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var owner = Leaf("stf", "m4/study/stf.xml", current ? scenario == "replace" ? "replace" : "append" : "new");
            if (current) owner.SetAttributeValue("modified-file", "../0000/index.xml#" + (scenario == "missing-target" ? "absent" : "stf"));
            var section = new XElement("m4-3-literature-references", owner);
            if (!current) section.Add(Leaf("content", "m4/study/report.pdf", "new"));
            if (current && scenario == "multiple-owners") section.Add(Leaf("alias", "m4/study/stf.xml", "new"));
            if (current && scenario is "competing-update" or "known-empty")
            {
                var other = Leaf("other", "m4/study/other.xml", scenario == "competing-update" ? "append" : "new");
                if (scenario == "competing-update") other.SetAttributeValue("modified-file", "../0000/index.xml#stf");
                section.Add(other);
            }
            if (current && scenario == "content-deleted") section.Add(new XElement("leaf", new XAttribute("ID", "deleted"),
                new XAttribute("operation", "delete"), new XAttribute("checksum", ""), new XAttribute("checksum-type", "md5"),
                new XAttribute("modified-file", "../0000/index.xml#content"), new XElement("title", "deleted")));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null),
                new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                    new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                    new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml", "new")),
                    new XElement("m4-nonclinical-study-reports", section)));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index.ToString());
            var title = current && scenario is not ("unchanged" or "category" or "category-order") ? "Corrected title" : "Original title";
            var species = current && scenario == "category" ? "mouse" : "rat";
            var categories = "<category name='species' info-type='ich'>" + species + "</category><category name='route-of-admin' info-type='ich'>oral</category>";
            if (current && scenario == "category-removed") categories = "";
            if (current && scenario == "category-order") categories = "<category name='route-of-admin' info-type='ich'>oral</category><category name='species' info-type='ich'>rat</category>";
            var stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'>" +
                "<study-identifier><title>" + title + "</title><study-id>" + (current && scenario == "study-id-change" ? "B" : "A") + "</study-id>" + categories + "</study-identifier>" +
                "<study-document>" + (current ? scenario == "empty-block" ? "<content-block><block-title>Empty</block-title></content-block>" : "" :
                    scenario == "previous-empty" ? "" : "<doc-content xlink:href='../../index.xml#" + (scenario == "previous-missing-leaf" ? "absent" : "content") + "'/>") + "</study-document></ectd:study>";
            if (!current && scenario == "previous-tail") stf = stf[..^8];
            _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
            if (current && scenario is "competing-update" or "known-empty") _files[sequence + "/m4/study/other.xml"] = Encoding.UTF8.GetBytes(stf);
            if (!current) _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 report"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string operation) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", operation),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1816-revision-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => Armed ? throw new InvalidOperationException("Use frozen XML.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Armed ? throw new InvalidOperationException("Use frozen identities.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
