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

public sealed class FdaStfReferenceCoverageRuleTests
{
    [Theory]
    [InlineData("no-stf", CheckStatus.Fail, 2)]
    [InlineData("covers-all", CheckStatus.Pass, 0)]
    [InlineData("covers-first", CheckStatus.Fail, 1)]
    [InlineData("same-path", CheckStatus.Fail, 1)]
    [InlineData("orphan-stf", CheckStatus.Fail, 2)]
    [InlineData("only-stf", CheckStatus.NotApplicable, 0)]
    [InlineData("delete", CheckStatus.NotApplicable, 0)]
    [InlineData("append", CheckStatus.Fail, 2)]
    [InlineData("replace", CheckStatus.Fail, 2)]
    [InlineData("covered-append", CheckStatus.Pass, 0)]
    [InlineData("covered-replace", CheckStatus.Pass, 0)]
    [InlineData("split-stfs", CheckStatus.Pass, 0)]
    [InlineData("normal-xml", CheckStatus.Fail, 2)]
    [InlineData("unknown-xml", CheckStatus.NotEvaluated, 0)]
    [InlineData("truncated-stf", CheckStatus.NotEvaluated, 0)]
    [InlineData("index-tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("invalid-reference", CheckStatus.Fail, 1)]
    [InlineData("missing-history-reference", CheckStatus.Fail, 1)]
    [InlineData("duplicate-reference", CheckStatus.Fail, 1)]
    [InlineData("nested-reference", CheckStatus.Pass, 0)]
    [InlineData("node-extension", CheckStatus.Pass, 0)]
    [InlineData("cross-heading", CheckStatus.NotEvaluated, 0)]
    [InlineData("cross-extension", CheckStatus.NotEvaluated, 0)]
    [InlineData("unrelated-history-unknown", CheckStatus.Fail, 2)]
    [InlineData("reuse-old-bytes", CheckStatus.Fail, 1)]
    [InlineData("history-stf", CheckStatus.Fail, 2)]
    [InlineData("history-future", CheckStatus.Fail, 2)]
    public async Task RequiresExactPerLeafCoverageNotNearbyStfPresence(string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        foreach (var finding in check.Findings)
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.True(finding.Location.LeafId is "content-a" or "content-b");
        }
        if (count == 1) Assert.Equal("content-b", Assert.Single(check.Findings).Location.LeafId);
    }

    [Theory]
    [InlineData("m4.2.3.1", CheckStatus.Fail)]
    [InlineData("m4.2", CheckStatus.Fail)]
    [InlineData("m5.3.7", CheckStatus.Fail)]
    [InlineData("m5.3", CheckStatus.Fail)]
    [InlineData("m4.3", CheckStatus.NotApplicable)]
    [InlineData("m5.2", CheckStatus.NotApplicable)]
    [InlineData("m5.4", CheckStatus.NotApplicable)]
    [InlineData("m5.3.6", CheckStatus.NotApplicable)]
    [InlineData("m4", CheckStatus.NotApplicable)]
    [InlineData("m5", CheckStatus.NotApplicable)]
    public async Task ScopeUsesExactStandardHeadingNotPhysicalDirectory(string section, CheckStatus expected)
    {
        var fixture = await Fixture("no-stf", section);
        Assert.Equal(expected, Inspect(fixture).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task SharedBudgetAndProgressAreBound(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("no-stf");
        var snapshots = new List<RuleCheckResult>();
        var check = FdaStfReferenceCoverageRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.Equal(expected, check!.Status);
        Assert.Equal(budget, check.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrKnownMissingCoverage(bool afterFailure)
    {
        var fixture = await Fixture("no-stf");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfReferenceCoverageRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2021-09-14", CheckStatus.NotApplicable)]
    [InlineData("valid", "2021-09-15", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("no-stf", variant: variant, date: date)).Status);

    [Theory]
    [InlineData("covers-all", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("covers-first", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task ReportRetainsPartialBoundaryAndKnownFailures(string scenario, CheckStatus component, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(component, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1789").Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1789").CheckStatus);
    }

    [Fact]
    public async Task OldCatalogCannotBorrowCurrentRule()
    {
        var fixture = await Fixture("no-stf");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1789"));
        Assert.Null(FdaStfReferenceCoverageRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    [Fact]
    public async Task ReferencedHistoricalContextMismatchRemainsUnknown()
    {
        var fixture = await Fixture("history-stf");
        var xml = fixture.Lifecycle.XmlInspections.Select(observation => observation.InputDigest == fixture.Packages.Target.InputDigest ? observation :
            new PackageXmlInspection(observation.InputDigest, observation.ProfileSnapshotId, observation.AssetManifestDigest, observation.LimitsDigest,
                observation.Backbones, observation.Checks, new string('f', 64), observation.StudyTaggingFiles));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, xml,
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.False(PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default).TargetScopeComplete);
        var result = FdaStfReferenceCoverageRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfReferenceCoverageRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string section = "m4.2.3.1", string variant = "valid", string? date = "2026-11-20")
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
        var target = new Capture(app, "0001", scenario, section, context);
        var history = scenario is "reuse-old-bytes" or "history-stf" or "history-future" or "unrelated-history-unknown" ?
            new Capture(app, "0000", scenario == "history-future" ? "future-source" : scenario == "unrelated-history-unknown" ? "unknown-history" : "covers-all", section, context) : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (scenario != "index-tail") Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        else Assert.False(index.ReadComplete);
        foreach (var stf in xml.StudyTaggingFiles.Documents)
        {
            if (scenario == "truncated-stf") Assert.False(stf.ReadComplete);
            else { Assert.True(stf.ReadComplete); Assert.Equal(CheckStatus.Pass, stf.DtdStatus); }
        }
        if (scenario == "unknown-xml") Assert.False(xml.StudyTaggingFiles.InventoryComplete);
        if (scenario == "truncated-stf") Assert.False(Assert.Single(xml.StudyTaggingFiles.Documents).ReadComplete);
        if (scenario is "history-stf" or "history-future" or "reuse-old-bytes")
        {
            var historyXml = Assert.Single(lifecycle.XmlInspections, observation => observation.InputDigest == history!.InputDigest);
            Assert.Equal(CheckStatus.Pass, Assert.Single(historyXml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
            var historyStf = Assert.Single(historyXml.StudyTaggingFiles.Documents);
            Assert.True(historyStf.ReadComplete);
            Assert.Equal(CheckStatus.Pass, historyStf.DtdStatus);
        }
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, default);
        if (scenario == "unrelated-history-unknown")
        {
            Assert.True(bindings.TargetScopeComplete);
            var bound = Assert.Single(bindings.Documents);
            Assert.Empty(bound.Document.Elements);
            Assert.DoesNotContain(bound.Owners, owner => owner.Leaf.Address?.SequenceNumber == "0001");
        }
        if (scenario == "cross-extension")
        {
            var bound = Assert.Single(bindings.Documents);
            Assert.True(Assert.Single(bound.Owners).Qualified);
            Assert.All(bound.References, reference => Assert.Equal(StfReferenceStatus.Resolved, reference.Status));
            Assert.All(index.Leaves.Where(leaf => leaf.Id is "content-a" or "content-b"), leaf => Assert.True(leaf.ContextComplete));
            Assert.NotEqual(Assert.Single(index.Leaves, leaf => leaf.Id == "stf").ParentNodePath,
                Assert.Single(index.Leaves, leaf => leaf.Id == "content-a").ParentNodePath);
        }
        if (scenario is "history-stf" or "history-future")
        {
            var bound = Assert.Single(bindings.Documents);
            Assert.Contains(bound.Owners, owner => owner.Qualified && owner.Leaf.Address!.SequenceNumber == "0001");
            Assert.Equal(2, bound.References.Count);
            Assert.All(bound.References, reference =>
            {
                Assert.Equal(scenario == "history-future" ? StfReferenceStatus.FutureReference : StfReferenceStatus.Resolved, reference.Status);
                Assert.Equal(scenario == "history-future" ? "0001" : "0000", reference.Target!.SequenceNumber);
            });
        }
        if (scenario == "reuse-old-bytes")
        {
            var current = Assert.Single(index.Leaves, leaf => leaf.Id == "content-b");
            Assert.Equal("0001", current.Address!.SequenceNumber);
            Assert.Equal("0000/m4/study/b.pdf", PackageLogicalPath.ResolveReference(index.LogicalPath, current.Href!).LogicalPath);
            Assert.Contains(Assert.Single(bindings.Documents, document => document.IsTarget).References,
                reference => reference.Status == StfReferenceStatus.Resolved && reference.Target!.SequenceNumber == "0000" && reference.Target.LeafId == "content-b");
        }
        if (scenario == "orphan-stf")
        {
            var bound = Assert.Single(bindings.Documents);
            Assert.Empty(bound.Owners);
            Assert.Equal(2, bound.References.Count);
            Assert.All(bound.References, reference => Assert.Equal(StfReferenceStatus.Resolved, reference.Status));
        }
        if (scenario is "invalid-reference" or "missing-history-reference")
        {
            var bound = Assert.Single(bindings.Documents);
            Assert.Contains(bound.References, reference => reference.Status == StfReferenceStatus.Resolved && reference.Target!.LeafId == "content-a");
            var additional = Assert.Single(bound.References, reference => reference.Status != StfReferenceStatus.Resolved);
            Assert.Equal(scenario == "invalid-reference" ? StfReferenceStatus.InvalidReference : StfReferenceStatus.HistoryUnavailable, additional.Status);
            if (scenario == "missing-history-reference") Assert.Equal("0000", additional.Target!.SequenceNumber);
        }
        if (scenario == "cross-heading")
        {
            var bound = Assert.Single(bindings.Documents);
            var owner = Assert.Single(bound.Owners);
            Assert.True(owner.Qualified);
            Assert.Equal("m4-2-3-2-repeat-dose-toxicity", owner.Heading);
            Assert.Equal(2, bound.References.Count);
            Assert.All(bound.References, reference => Assert.Equal(StfReferenceStatus.Resolved, reference.Status));
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string scenario, string section, PackageStandardsContext context)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var hasStf = scenario is not ("no-stf" or "delete" or "append" or "replace" or "normal-xml" or "unknown-xml" or "unrelated-history-unknown");
            var physicalStf = hasStf && scenario is not ("history-stf" or "history-future");
            var definition = context.NodeSchema.Definitions.Values.Single(node => node.SectionPath == section);
            var heading = new XElement(definition.DefinitionKey);
            var contentParent = heading;
            if (scenario is "node-extension" or "cross-extension") { contentParent = new XElement("node-extension", new XAttribute("ID", "extension"), new XElement("title", "Nested")); heading.Add(contentParent); }
            if (scenario != "only-stf")
            {
                var operation = scenario is "delete" or "append" or "replace" ? scenario : scenario == "covered-append" ? "append" : scenario == "covered-replace" ? "replace" : "new";
                contentParent.Add(Leaf("content-a", "m4/study/a.pdf", operation));
                var secondPath = scenario switch { "same-path" => "m4/study/a.pdf", "normal-xml" or "unknown-xml" => "m4/study/content.xml", "reuse-old-bytes" => "../0000/m4/study/b.pdf", _ => "m4/study/b.pdf" };
                contentParent.Add(Leaf("content-b", secondPath, operation));
            }
            XElement? otherHeading = null;
            if (hasStf && scenario != "orphan-stf")
            {
                var owner = Leaf("stf", scenario is "history-stf" or "history-future" ? "../0000/m4/study/stf.xml" : "m4/study/stf.xml");
                owner.SetAttributeValue("version", "STF Version 2.2");
                if (scenario == "cross-heading") otherHeading = new XElement("m4-2-3-2-repeat-dose-toxicity", owner);
                else if (scenario == "cross-extension") heading.Add(new XElement("node-extension", new XAttribute("ID", "owner-extension"), new XElement("title", "Other instance"), owner));
                else contentParent.Add(owner);
                if (scenario == "split-stfs") contentParent.Add(Leaf("stf-second", "m4/study/stf-second.xml"));
            }
            XElement branch = heading;
            while (definition.ParentDefinitionKey is { } parent)
            {
                branch = new XElement(parent, branch);
                if (parent == "m4-2-3-toxicology" && otherHeading is not null) branch.Add(otherHeading);
                definition = context.NodeSchema.Get(parent);
            }
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), branch);
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var references = new List<string>();
            if (scenario != "only-stf")
            {
                references.Add("../../index.xml#content-a");
                if (scenario is "covers-all" or "orphan-stf" or "cross-heading" or "cross-extension" or "node-extension" or "nested-reference" or "covered-append" or "covered-replace") references.Add("../../index.xml#content-b");
                if (scenario == "invalid-reference") references.Add("https://invalid.example/other.xml#other");
                if (scenario == "missing-history-reference") references.Add("../../../0000/index.xml#absent");
                if (scenario == "duplicate-reference") references.Add("../../index.xml#content-a");
                if (scenario == "reuse-old-bytes") references.Add("../../../0000/index.xml#content-b");
                if (scenario == "future-source") { references.Clear(); references.Add("../../../0001/index.xml#content-a"); references.Add("../../../0001/index.xml#content-b"); }
            }
            var content = string.Concat(references.Select(href => "<doc-content xlink:href='" + href + "'/>"));
            if (scenario == "nested-reference") content = "<content-block><block-title>Nested</block-title>" + content + "</content-block>";
            var stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" +
                "<study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document>" + content + "</study-document></ectd:study>";
            if (scenario == "truncated-stf") stf = stf[..^8];
            if (scenario == "unknown-history") stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><other/>";
            if (physicalStf) _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "split-stfs") _files[sequence + "/m4/study/stf-second.xml"] = Encoding.UTF8.GetBytes(stf.Replace("#content-a", "#content-b", StringComparison.Ordinal));
            if (scenario is "normal-xml" or "unknown-xml") _files[sequence + "/m4/study/content.xml"] = Encoding.UTF8.GetBytes(scenario == "normal-xml" ? "<other/>" : "<unknown");
            _files[sequence + "/m4/study/a.pdf"] = "%PDF-1.4 A"u8.ToArray();
            _files[sequence + "/m4/study/b.pdf"] = "%PDF-1.4 B"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string operation = "new")
        {
            var leaf = new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)),
                new XAttribute("checksum-type", "md5"), new XElement("title", id));
            if (operation != "delete") leaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), href);
            if (operation != "new") leaf.SetAttributeValue("modified-file", "../0000/index.xml#old");
            return leaf;
        }
        public bool Armed { get; set; }
        public string SourceId => "fda1789-" + Manifest.SequenceNumber;
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
