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

public sealed class FdaStfReferencedLeafRuleTests
{
    [Theory]
    [InlineData("exists", CheckStatus.Pass, 0)]
    [InlineData("missing", CheckStatus.Fail, 1)]
    [InlineData("nonleaf", CheckStatus.Fail, 1)]
    [InlineData("exists-and-missing", CheckStatus.Fail, 1)]
    [InlineData("missing-and-unknown", CheckStatus.Fail, 1)]
    [InlineData("two-missing", CheckStatus.Fail, 2)]
    [InlineData("missing-history", CheckStatus.NotEvaluated, 0)]
    [InlineData("history-incomplete", CheckStatus.NotEvaluated, 0)]
    [InlineData("history-missing", CheckStatus.Fail, 1)]
    [InlineData("history-exists", CheckStatus.Pass, 0)]
    [InlineData("history-later-deleted", CheckStatus.Pass, 0)]
    [InlineData("direct-delete", CheckStatus.Pass, 0)]
    [InlineData("missing-content-file", CheckStatus.Pass, 0)]
    [InlineData("invalid-uri", CheckStatus.NotEvaluated, 0)]
    [InlineData("no-fragment", CheckStatus.NotEvaluated, 0)]
    [InlineData("future", CheckStatus.NotEvaluated, 0)]
    [InlineData("index-tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("source-tail", CheckStatus.Fail, 1)]
    [InlineData("orphan", CheckStatus.Fail, 1)]
    [InlineData("historical-source", CheckStatus.Fail, 1)]
    [InlineData("historical-only", CheckStatus.NotApplicable, 0)]
    [InlineData("no-references", CheckStatus.Pass, 0)]
    [InlineData("no-stf", CheckStatus.NotApplicable, 0)]
    public async Task MissingIsDifferentFromUnavailableInactiveOrMissingContent(string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        foreach (var finding in check.Findings)
        {
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("doc-content/@xlink:href", finding.Location.FieldPath);
            Assert.Null(finding.Location.LeafId);
            var sequence = scenario == "historical-source" ? "0000" : "0001";
            Assert.Equal(sequence + "/m4/study/stf.xml", finding.Location.LogicalPath);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == finding.Location.LogicalPath && file.SourceId == fixture.Packages.Sequences[sequence].SourceId);
        }
    }

    [Theory]
    [InlineData("missing-index-entry")]
    [InlineData("missing-index-and-summary")]
    [InlineData("missing-original-element")]
    [InlineData("summary-id-mismatch")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-original-id")]
    [InlineData("duplicate-stf")]
    [InlineData("source-hash")]
    [InlineData("context")]
    [InlineData("broken-reference-chain")]
    public async Task ContradictoryFactsCannotProveLeafExistenceOrAbsence(string scenario)
    {
        var fixture = await Fixture(scenario == "broken-reference-chain" ? "missing" : "exists");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var ids = index.IdIndex.Values.SelectMany(entries => entries).ToArray();
        var changed = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier,
            index.Elements.Where(element => scenario != "missing-original-element" || element.Attribute("ID") != "content")
                .Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                    element.Attributes.Values.Select(attribute => scenario == "duplicate-original-id" && attribute.Name == "ID" && attribute.Value == "heading" ?
                        attribute with { Value = "content", ExplicitValue = "content" } : attribute), element.Children, element.Text, element.Location)), index.Nodes,
            index.Leaves.Where(leaf => scenario != "missing-index-and-summary" || leaf.Id != "content")
                .Select(leaf => scenario == "summary-id-mismatch" && leaf.Id == "content" ? leaf with { Id = "other" } : leaf),
            scenario is "missing-index-entry" or "missing-index-and-summary" ? ids.Where(id => id.Id != "content") : scenario == "duplicate-id" ? ids.Concat(ids.Where(id => id.Id == "content")) : ids,
            index.ResolvedAssets);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var reference = Assert.Single(stf.Elements, element => element.LocalName == "doc-content");
        var elements = stf.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
            element.NamespaceUri, element.Attributes.Values, scenario == "broken-reference-chain" && element.NodePath == reference.ParentPath ?
                element.Children.Where(child => child != reference.NodePath) : element.Children, element.Text, element.Location));
        var changedStf = new ParsedStudyTaggingFile(stf.LogicalPath, scenario == "source-hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changed), xml.Checks,
            scenario == "context" ? new string('f', 64) : xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, scenario == "duplicate-stf" ? [changedStf, changedStf] : [changedStf]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var result = FdaStfReferencedLeafRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task BrokenReferenceChainCannotEraseAnotherKnownMissingLeaf()
    {
        var fixture = await Fixture("two-missing");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var first = stf.Elements.First(element => element.LocalName == "doc-content");
        var elements = stf.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
            element.NamespaceUri, element.Attributes.Values, element.NodePath == first.ParentPath ? element.Children.Where(child => child != first.NodePath) : element.Children,
            element.Text, element.Location));
        var changed = new ParsedStudyTaggingFile(stf.LogicalPath, stf.CapturedSha256, stf.ReadComplete, stf.DtdStatus,
            stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, xml.RulesDigest, new(xml.StudyTaggingFiles.InventoryComplete, [changed]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var bound = Assert.Single(PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default).Documents);
        Assert.False(bound.StructureComplete);
        Assert.Equal(StfReferenceStatus.LeafMissing, Assert.Single(bound.References).Status);
        var check = FdaStfReferencedLeafRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Single(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task SharedBudgetAndSnapshotsRemainBound(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-missing");
        var snapshots = new List<RuleCheckResult>();
        var result = FdaStfReferencedLeafRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); });
        Assert.Equal(expected, result!.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrKnownMissingLeaf(bool afterFailure)
    {
        var fixture = await Fixture("missing");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfReferencedLeafRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("missing", variant, date)).Status);

    [Theory]
    [InlineData("exists", CheckStatus.Pass)]
    [InlineData("missing", CheckStatus.Fail)]
    public async Task DeliveryAndReportExposeExistenceRule(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1833").Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1833").CheckStatus);
    }

    [Fact]
    public async Task OldCatalogCannotBorrowCurrentRule()
    {
        var fixture = await Fixture("missing");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1833"));
        Assert.Null(FdaStfReferencedLeafRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfReferencedLeafRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string variant = "valid", string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant == "missing-source")
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var history = scenario is "history-incomplete" or "history-missing" or "history-exists" or "history-later-deleted" or "historical-source" or "historical-only" ?
            new Capture(app, "0000", scenario == "history-incomplete" ? "index-tail" : scenario is "historical-source" or "historical-only" ? "missing" : "exists") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
            if (scenario == "index-tail" || scenario == "history-incomplete" && xml.InputDigest == history!.InputDigest) Assert.False(index.ReadComplete);
            else Assert.Equal(CheckStatus.Pass, index.DtdStatus);
            foreach (var stf in xml.StudyTaggingFiles.Documents)
            {
                Assert.Single(stf.Elements, element => element.ParentPath is null && element.LocalName == "study");
                if (scenario == "source-tail") Assert.False(stf.ReadComplete);
                else Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
            }
        }
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, default);
        if (scenario is "missing-history" or "history-incomplete" or "history-missing" or "direct-delete" or "nonleaf")
        {
            var expected = scenario switch { "missing-history" => StfReferenceStatus.HistoryUnavailable, "history-incomplete" => StfReferenceStatus.BackboneIncomplete,
                "history-missing" => StfReferenceStatus.LeafMissing, "direct-delete" => StfReferenceStatus.LeafUnqualified, _ => StfReferenceStatus.NotLeaf };
            Assert.Equal(expected, Assert.Single(Assert.Single(bindings.Documents, document => document.IsTarget).References).Status);
        }
        if (scenario == "historical-source") Assert.Contains(Assert.Single(bindings.Documents).Owners, owner => owner.Qualified && owner.Leaf.Address!.SequenceNumber == "0001");
        if (scenario == "history-later-deleted")
        {
            var reference = Assert.Single(Assert.Single(bindings.Documents, document => document.IsTarget).References);
            Assert.Equal(StfReferenceStatus.Resolved, reference.Status);
            Assert.Equal("0000", reference.Target!.SequenceNumber);
            Assert.Equal(PackageLeafEffectiveness.Deleted, Assert.Single(lifecycle.States, state => state.Address == reference.Target).Effectiveness);
        }
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
            if (scenario is not ("orphan" or "historical-only" or "no-stf")) section.Add(Leaf("stf", scenario == "historical-source" ? "../0000/m4/study/stf.xml" : "m4/study/stf.xml"));
            section.Add(Leaf("content", "m4/study/content.pdf", scenario == "direct-delete" ? "delete" : "new"));
            if (scenario == "history-later-deleted") section.Add(Leaf("deletion", "m4/study/content.pdf", "delete", "../0000/index.xml#content"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var href = scenario switch
            {
                "missing" or "source-tail" or "orphan" or "two-missing" or "missing-and-unknown" => "../../index.xml#absent",
                "nonleaf" => "../../index.xml#heading", "missing-history" or "history-incomplete" or "history-exists" or "history-later-deleted" => "../../../0000/index.xml#content",
                "history-missing" => "../../../0000/index.xml#absent", "future" => "../../../0002/index.xml#content",
                "invalid-uri" => "https://invalid.example/other#content", "no-fragment" => "../../index.xml", _ => "../../index.xml#content"
            };
            var content = scenario == "no-references" ? "" : "<doc-content xlink:href='" + href + "'/>";
            if (scenario == "exists-and-missing") content += "<doc-content xlink:href='../../index.xml#absent'/>";
            if (scenario == "two-missing") content += "<doc-content xlink:href='../../index.xml#absent2'/>";
            if (scenario == "missing-and-unknown") content += "<doc-content xlink:href='../../../0000/index.xml#unknown'/>";
            var stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" +
                "<study-identifier><title>Study</title><study-id>A</study-id></study-identifier><study-document>" + content + "</study-document></ectd:study>";
            if (scenario == "source-tail") stf = stf[..^8];
            if (scenario is not ("historical-source" or "historical-only" or "no-stf")) _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
            if (scenario != "missing-content-file") _files[sequence + "/m4/study/content.pdf"] = "%PDF-1.4 example"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string operation = "new", string modified = "../0000/index.xml#old")
        {
            var leaf = new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", operation == "delete" ? "" : new string('0', 32)),
                new XAttribute("checksum-type", "md5"), new XAttribute("version", "STF Version 2.2"), new XElement("title", id));
            if (operation != "delete") leaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), href);
            if (operation != "new") leaf.SetAttributeValue("modified-file", modified);
            return leaf;
        }
        public bool Armed { get; set; }
        public string SourceId => "fda1833-" + Manifest.SequenceNumber;
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
