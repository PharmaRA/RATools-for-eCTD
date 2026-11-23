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

public sealed class FdaStfMultiSectionReferenceRuleTests
{
    private const string RuleId = "FDA47-1990";

    [Theory]
    [InlineData("single", CheckStatus.Pass)]
    [InlineData("same-section", CheckStatus.Pass)]
    [InlineData("cross-section", CheckStatus.Fail)]
    [InlineData("alias", CheckStatus.Fail)]
    [InlineData("different-files", CheckStatus.Pass)]
    [InlineData("extensions", CheckStatus.NotEvaluated)]
    [InlineData("same-extension", CheckStatus.Pass)]
    [InlineData("unreferenced", CheckStatus.NotApplicable)]
    [InlineData("delete-second", CheckStatus.Pass)]
    [InlineData("history", CheckStatus.Fail)]
    [InlineData("history-single", CheckStatus.Pass)]
    [InlineData("history-only", CheckStatus.NotApplicable)]
    [InlineData("history-missing", CheckStatus.NotEvaluated)]
    [InlineData("history-missing-nonxml", CheckStatus.NotEvaluated)]
    [InlineData("missing", CheckStatus.NotEvaluated)]
    [InlineData("fragment", CheckStatus.NotEvaluated)]
    [InlineData("tail", CheckStatus.Fail)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("ordinary", CheckStatus.NotApplicable)]
    [InlineData("known-and-unknown", CheckStatus.Fail)]
    public async Task ExactFileIdentityAndCurrentSectionsAreSeparate(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal("owner2", finding.Location.LeafId);
            Assert.Contains("m4.2", finding.Actual!);
            Assert.Contains("m4.3", finding.Actual!);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == (scenario == "history" ? "0000" : "0001") + "/m4/study/stf-A.xml");
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("derived-delete")]
    [InlineData("address")]
    [InlineData("profile")]
    [InlineData("broken-parent")]
    [InlineData("hash")]
    [InlineData("context")]
    public async Task AmbiguousOwnerOrContentCannotProveCrossSectionReuse(string variant)
    {
        var fixture = await Fixture("cross-section");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, backbone => backbone.Kind == BackboneKind.Ich);
        var second = Assert.Single(index.Elements, element => element.Attribute("ID") == "owner2");
        var elements = index.Elements.Select(element => variant == "broken-parent" && element.NodePath == second.ParentPath ?
            new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values,
                element.Children.Where(path => path != second.NodePath), element.Text, element.Location) : element);
        var changedIndex = new ParsedBackbone(index.LogicalPath, variant == "profile" ? PackageValidationCatalog.IchProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id != "owner2" ? leaf : variant == "derived-delete" ? leaf with { Operation = "delete" } :
                variant == "address" ? leaf with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0000", "index.xml", "owner2") } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var changedStf = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(backbone => backbone.Kind != BackboneKind.Ich).Append(changedIndex), xml.Checks,
            variant == "context" ? new string('f', 64) : xml.RulesDigest, new(xml.StudyTaggingFiles.InventoryComplete, [changedStf])));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("valid", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", "2013-04-30", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("cross-section", variant, date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task PerFileFindingsShareBudgetAndImmutableProgress(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-files-cross");
        var snapshots = new List<RuleCheckResult>();
        var result = Inspect(fixture, budget, check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); })!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownReuse(bool afterFailure)
    {
        var fixture = await Fixture("cross-section");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: check =>
        { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Fact]
    public async Task SameSectionDoesNotSpendFindingBudget()
        => Assert.Equal(CheckStatus.Pass, Inspect(await Fixture("same-section"), 0)!.Status);

    [Fact]
    public async Task AlreadyCancelledTokenPropagates()
    {
        var fixture = await Fixture("single");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token));
    }

    [Theory]
    [InlineData("single", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("cross-section", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("unreferenced", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    public async Task ReportRetainsUnqualifiedSectionSemantics(string scenario, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(delivery.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowNewRule()
    {
        var fixture = await Fixture("cross-section");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfMultiSectionReferenceRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static FixtureData ReplaceXml(FixtureData fixture, PackageXmlInspection changed) => fixture with
    { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != changed.InputDigest).Append(changed),
        fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };

    private static async Task<FixtureData> Fixture(string scenario, string variant = "valid", string? date = "2026-11-23")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant == "missing-source")
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var history = scenario is "history" or "history-only" or "history-single" ? new Capture(app, "0000", "cross-section") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var currentXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == target.InputDigest);
        var selectedXml = history is null ? currentXml : Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == history.InputDigest);
        if (scenario is "ordinary" or "missing" or "history-missing" or "history-missing-nonxml") Assert.Empty(selectedXml.StudyTaggingFiles.Documents);
        else
        {
            Assert.Equal(scenario is "different-files" or "two-files-cross" ? 2 : 1, selectedXml.StudyTaggingFiles.Documents.Count);
            Assert.Contains(selectedXml.StudyTaggingFiles.Documents, document => document.LogicalPath == (history is null ? "0001" : "0000") + "/m4/study/stf-A.xml");
        }
        if (history is not null) Assert.Empty(currentXml.StudyTaggingFiles.Documents);
        if (scenario == "history-missing-nonxml")
        {
            Assert.Empty(packages.Baseline.Entries);
            Assert.True(PackageStfBindingReader.Read(packages, lifecycle, context, default).TargetScopeComplete);
        }
        foreach (var xml in lifecycle.XmlInspections)
        {
            var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
            Assert.Equal(CheckStatus.Pass, index.DtdStatus);
            Assert.True(index.ReadComplete);
            foreach (var stf in xml.StudyTaggingFiles.Documents)
            {
                if (scenario == "tail") { Assert.False(stf.ReadComplete); Assert.Single(stf.Elements, element => element.ParentPath is null && element.LocalName == "study"); }
                else if (scenario == "wrong-root") Assert.Empty(stf.Elements);
                else { Assert.True(stf.ReadComplete); Assert.Equal(CheckStatus.Pass, stf.DtdStatus); }
            }
        }
        if (scenario is "different-files" or "two-files-cross")
        {
            var docs = Assert.Single(lifecycle.XmlInspections).StudyTaggingFiles.Documents;
            Assert.Equal(2, docs.Count);
            Assert.Equal(docs[0].CapturedSha256, docs[1].CapturedSha256);
            Assert.NotEqual(docs[0].LogicalPath, docs[1].LogicalPath);
        }
        if (scenario == "alias")
        {
            var index = Assert.Single(Assert.Single(lifecycle.XmlInspections).Backbones, document => document.Kind == BackboneKind.Ich);
            var first = Assert.Single(index.Leaves, leaf => leaf.Id == "owner");
            var second = Assert.Single(index.Leaves, leaf => leaf.Id == "owner2");
            Assert.NotEqual(first.Href, second.Href);
            Assert.Equal(PackageLogicalPath.ResolveReference(index.LogicalPath, first.Href!).LogicalPath, PackageLogicalPath.ResolveReference(index.LogicalPath, second.Href!).LogicalPath);
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
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var name = scenario == "ordinary" ? "metadata.xml" : scenario == "history-missing-nonxml" ? "stf-A.txt" : "stf-A.xml";
            var href = (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name;
            var first = new XElement("m4-2-study-reports");
            var second = new XElement("m4-3-literature-references");
            if (scenario is not ("unreferenced" or "history-only"))
            {
                var owner = Leaf("owner", href);
                var owner2 = Leaf("owner2", scenario == "alias" ? "m4/study/stf-%41.xml" : scenario == "different-files" ? "m4/study/stf-B.xml" : href + (scenario == "fragment" ? "#bad" : ""), scenario == "delete-second" ? "delete" : "new");
                if (scenario is "extensions" or "same-extension")
                {
                    var extension = new XElement("node-extension", new XAttribute("ID", "ext1"), new XElement("title", "One"), owner);
                    second.Add(extension);
                    if (scenario == "same-extension") extension.Add(owner2);
                    else second.Add(new XElement("node-extension", new XAttribute("ID", "ext2"), new XElement("title", "Two"), owner2));
                }
                else
                {
                    first.Add(owner);
                    if (scenario is not ("single" or "history-single"))
                    { if (scenario == "same-section") first.Add(owner2); else second.Add(owner2); }
                }
                if (scenario == "two-files-cross") { first.Add(Leaf("second1", "m4/study/stf-B.xml")); second.Add(Leaf("second2", "m4/study/stf-B.xml")); }
                if (scenario == "known-and-unknown") second.Add(Leaf("unknown", "../0000/m4/study/absent.xml"));
            }
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), new XElement("m4-nonclinical-study-reports", first, second));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document/></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            if (scenario is "different-files" or "two-files-cross") _files[sequence + "/m4/study/stf-B.xml"] = Encoding.UTF8.GetBytes(stf);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string operation = "new") => new("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1990-" + Manifest.SequenceNumber;
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
