using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfLeafAssociationRuleTests
{
    [Theory]
    [InlineData("empty-new", CheckStatus.Fail, 1)]
    [InlineData("empty-block", CheckStatus.Fail, 1)]
    [InlineData("resolved", CheckStatus.Pass, 0)]
    [InlineData("resolved-unknown", CheckStatus.Pass, 0)]
    [InlineData("resolved-invalid", CheckStatus.Pass, 0)]
    [InlineData("invalid", CheckStatus.Fail, 1)]
    [InlineData("missing-leaf", CheckStatus.Fail, 1)]
    [InlineData("nonleaf", CheckStatus.Fail, 1)]
    [InlineData("future", CheckStatus.Fail, 1)]
    [InlineData("missing-history", CheckStatus.NotEvaluated, 0)]
    [InlineData("empty-append", CheckStatus.NotEvaluated, 0)]
    [InlineData("empty-replace", CheckStatus.NotEvaluated, 0)]
    [InlineData("orphan-empty", CheckStatus.NotEvaluated, 0)]
    [InlineData("orphan-resolved", CheckStatus.Pass, 0)]
    [InlineData("index-tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("stf-tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("self", CheckStatus.Pass, 0)]
    [InlineData("two-empty", CheckStatus.Fail, 2)]
    [InlineData("empty-and-unknown", CheckStatus.Fail, 1)]
    [InlineData("resolved-and-empty", CheckStatus.Fail, 1)]
    [InlineData("historical-only", CheckStatus.NotApplicable, 0)]
    [InlineData("historical-source", CheckStatus.Fail, 1)]
    [InlineData("no-stf", CheckStatus.NotApplicable, 0)]
    [InlineData("multiple-owners", CheckStatus.Fail, 1)]
    [InlineData("new-with-modified", CheckStatus.NotEvaluated, 0)]
    [InlineData("mixed-owners", CheckStatus.NotEvaluated, 0)]
    [InlineData("unknown-and-empty", CheckStatus.Fail, 1)]
    public async Task AtLeastOneLeafIsDistinctFromAllReferencesBeingValid(string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        foreach (var finding in check.Findings)
        {
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("study-document/doc-content", finding.Location.FieldPath);
            var sequence = scenario == "historical-source" ? "0000" : "0001";
            Assert.StartsWith(sequence + "/m4/study/", finding.Location.LogicalPath, StringComparison.Ordinal);
            Assert.Equal(fixture.Packages.Sequences[sequence].SourceId, Assert.Single(finding.Location.Files!).SourceId);
        }
        if (scenario == "resolved-and-empty") Assert.Equal("0001/m4/study/stf-b.xml", Assert.Single(check.Findings).Location.LogicalPath);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("hash")]
    [InlineData("context")]
    [InlineData("broken-reference-chain")]
    public async Task IncompleteOrContradictoryFactsCannotProveAbsence(string scenario)
    {
        var fixture = await Fixture(scenario == "broken-reference-chain" ? "resolved" : "empty-new");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var original = Assert.Single(xml.StudyTaggingFiles.Documents);
        var reference = original.Elements.FirstOrDefault(element => element.LocalName == "doc-content");
        var elements = original.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values,
            scenario == "broken-reference-chain" && element.NodePath == reference!.ParentPath ? element.Children.Where(child => child != reference.NodePath) : element.Children,
            element.Text, element.Location));
        var changed = new ParsedStudyTaggingFile(original.LogicalPath, scenario == "hash" ? new string('f', 64) : original.CapturedSha256,
            original.ReadComplete, original.DtdStatus, original.SystemIdentifier, elements, original.ProcessingInstructions, original.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, scenario == "context" ? new string('f', 64) : xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, scenario == "duplicate" ? [changed, changed] : [changed]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var result = FdaStfLeafAssociationRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("backbone-incomplete")]
    [InlineData("leaf-unqualified")]
    [InlineData("invalid-plus-unqualified")]
    public async Task CompositeReferenceUnknownsCannotProveNoLeaf(string scenario)
    {
        var fixture = await Fixture(scenario == "invalid-plus-unqualified" ? "resolved-invalid" : "resolved");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var changed = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind,
            scenario != "backbone-incomplete" && index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier,
            index.Elements, index.Nodes, index.Leaves.Select(leaf => scenario != "backbone-incomplete" && leaf.Id == "content" ? leaf with { Operation = "delete" } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changed), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var bound = Assert.Single(PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default).Documents);
        Assert.Contains(bound.References, reference => reference.Status == (scenario == "backbone-incomplete" ? StfReferenceStatus.BackboneIncomplete : StfReferenceStatus.LeafUnqualified));
        if (scenario == "invalid-plus-unqualified") Assert.Contains(bound.References, reference => reference.Status == StfReferenceStatus.InvalidReference);
        var result = FdaStfLeafAssociationRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task SharedBudgetAndProgressAreBound(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-empty");
        var snapshots = new List<RuleCheckResult>();
        var result = FdaStfLeafAssociationRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); });
        Assert.Equal(expected, result!.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrEstablishedAbsence(bool afterFailure)
    {
        var fixture = await Fixture("empty-new");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfLeafAssociationRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateRemainBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("empty-new", variant, date)).Status);

    [Theory]
    [InlineData("resolved", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("empty-new", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task ReportKeepsPartialExceptionBoundary(string scenario, CheckStatus component, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(component, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1816").Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1816").CheckStatus);
    }

    [Fact]
    public async Task OldCatalogCannotBorrowCurrentRule()
    {
        var fixture = await Fixture("empty-new");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1816"));
        Assert.Null(FdaStfLeafAssociationRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfLeafAssociationRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string variant = "valid", string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var history = scenario is "historical-source" or "historical-only" ? new Capture(app, "0000", "empty-new") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
            if (scenario == "index-tail") Assert.False(index.ReadComplete);
            else Assert.Equal(CheckStatus.Pass, index.DtdStatus);
            foreach (var document in xml.StudyTaggingFiles.Documents)
            {
                if (scenario == "empty-and-unknown" && document.LogicalPath.EndsWith("stf-b.xml", StringComparison.Ordinal) ||
                    scenario == "unknown-and-empty" && document.LogicalPath.EndsWith("stf-a.xml", StringComparison.Ordinal)) continue;
                Assert.Single(document.Elements, element => element.ParentPath is null && element.LocalName == "study");
                if (scenario == "stf-tail") Assert.False(document.ReadComplete);
                else Assert.Equal(CheckStatus.Pass, document.DtdStatus);
            }
        }
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, default);
        if (scenario == "self")
        {
            var reference = Assert.Single(Assert.Single(bindings.Documents).References);
            Assert.Equal(StfReferenceStatus.Resolved, reference.Status);
            Assert.Equal("stf", reference.Target!.LeafId);
        }
        if (scenario is "resolved-unknown" or "resolved-invalid")
        {
            var bound = Assert.Single(bindings.Documents);
            Assert.True(bound.StructureComplete);
            Assert.False(bound.Complete);
            Assert.Contains(bound.References, reference => reference.Status == StfReferenceStatus.Resolved);
        }
        if (scenario is "invalid" or "missing-leaf" or "nonleaf" or "future" or "missing-history")
        {
            var expected = scenario switch { "invalid" => StfReferenceStatus.InvalidReference, "missing-leaf" => StfReferenceStatus.LeafMissing,
                "nonleaf" => StfReferenceStatus.NotLeaf, "future" => StfReferenceStatus.FutureReference, _ => StfReferenceStatus.HistoryUnavailable };
            Assert.Equal(expected, Assert.Single(Assert.Single(bindings.Documents).References).Status);
        }
        if (scenario == "historical-source") Assert.Contains(Assert.Single(bindings.Documents).Owners, owner => owner.Qualified && owner.Leaf.Address!.SequenceNumber == "0001");
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var section = new XElement("m4-3-literature-references", new XAttribute("ID", "heading"));
            if (scenario is not ("orphan-empty" or "orphan-resolved" or "historical-only" or "no-stf"))
            {
                var owner = Leaf("stf", scenario == "historical-source" ? "../0000/m4/study/stf-a.xml" : "m4/study/stf-a.xml",
                    scenario == "empty-append" ? "append" : scenario == "empty-replace" ? "replace" : "new");
                if (scenario == "new-with-modified") owner.SetAttributeValue("modified-file", "../0000/index.xml#prior-stf");
                section.Add(owner);
            }
            if (scenario == "multiple-owners") section.Add(Leaf("stf-alias", "m4/study/stf-a.xml"));
            if (scenario == "mixed-owners") section.Add(Leaf("stf-alias", "m4/study/stf-a.xml", "append"));
            if (scenario is "two-empty" or "empty-and-unknown" or "unknown-and-empty" or "resolved-and-empty") section.Add(Leaf("stf-b", "m4/study/stf-b.xml"));
            section.Add(Leaf("content", "m4/study/content.pdf"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var href = scenario switch
            {
                "invalid" => "https://invalid.example/other", "missing-leaf" => "../../index.xml#absent", "nonleaf" => "../../index.xml#heading",
                "future" => "../../../0002/index.xml#content", "missing-history" => "../../../0000/index.xml#content", "self" => "../../index.xml#stf", _ => "../../index.xml#content"
            };
            var hasReference = scenario is "resolved" or "resolved-unknown" or "resolved-invalid" or "invalid" or "missing-leaf" or "nonleaf" or "future" or "missing-history" or "orphan-resolved" or "self" or "resolved-and-empty";
            var content = hasReference ? "<doc-content xlink:href='" + href + "'/>" : "";
            if (scenario == "resolved-unknown") content += "<doc-content xlink:href='../../../0000/index.xml#absent'/>";
            if (scenario == "resolved-invalid") content += "<doc-content xlink:href='https://invalid.example/other'/>";
            if (scenario == "empty-block") content = "<content-block><block-title>Empty</block-title></content-block>";
            var stf = Stf(content);
            if (scenario == "stf-tail") stf = stf[..^8];
            if (scenario == "unknown-and-empty") stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><other/>";
            if (scenario is not ("historical-source" or "historical-only" or "no-stf")) _files[sequence + "/m4/study/stf-a.xml"] = Encoding.UTF8.GetBytes(stf);
            if (scenario is "two-empty" or "unknown-and-empty" or "resolved-and-empty") _files[sequence + "/m4/study/stf-b.xml"] = Encoding.UTF8.GetBytes(Stf(""));
            if (scenario == "empty-and-unknown") _files[sequence + "/m4/study/stf-b.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><other/>");
            _files[sequence + "/m4/study/content.pdf"] = "%PDF-1.4 example"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Stf(string content) => "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" +
            "<study-identifier><title>Study</title><study-id>A</study-id></study-identifier><study-document>" + content + "</study-document></ectd:study>";
        private static XElement Leaf(string id, string href, string operation = "new")
        {
            var leaf = new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)),
                new XAttribute("checksum-type", "md5"), new XAttribute("version", "STF Version 2.2"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
            if (operation != "new") leaf.SetAttributeValue("modified-file", "../0000/index.xml#prior-stf");
            return leaf;
        }
        public bool Armed { get; set; }
        public string SourceId => "fda1816-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => Armed ? throw new InvalidOperationException("Use frozen XML only.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Armed ? throw new InvalidOperationException("Use frozen identity only.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
