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

public sealed class FdaStfStudyIdContinuityRuleTests
{
    [Theory]
    [InlineData("A", "A", CheckStatus.Pass)]
    [InlineData("A", "B", CheckStatus.Fail)]
    [InlineData("A", "a", CheckStatus.Fail)]
    [InlineData("A", " A", CheckStatus.Fail)]
    [InlineData("A", "A ", CheckStatus.Fail)]
    [InlineData("A1", "A1", CheckStatus.Pass)]
    [InlineData("", "", CheckStatus.NotEvaluated)]
    [InlineData(" ", " ", CheckStatus.NotEvaluated)]
    [InlineData("A", "", CheckStatus.NotEvaluated)]
    public async Task ComparesCompleteDecodedStudyIdsExactly(string previous, string current, CheckStatus expected)
    {
        var check = Inspect(await Fixture(previous, current));
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal(previous, finding.Expected);
            Assert.Equal(current, finding.Actual);
            Assert.Equal("0002/index.xml", finding.Location.LogicalPath);
            Assert.Equal("stf", finding.Location.LeafId);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("new", CheckStatus.NotApplicable)]
    [InlineData("replace", CheckStatus.NotApplicable)]
    [InlineData("delete", CheckStatus.NotApplicable)]
    [InlineData("missing-history", CheckStatus.NotEvaluated)]
    [InlineData("missing-modified", CheckStatus.NotEvaluated)]
    [InlineData("modified-query", CheckStatus.NotEvaluated)]
    [InlineData("future-predecessor", CheckStatus.NotEvaluated)]
    [InlineData("missing-predecessor", CheckStatus.NotEvaluated)]
    [InlineData("non-stf-predecessor", CheckStatus.NotEvaluated)]
    [InlineData("earlier-history-missing", CheckStatus.Fail)]
    [InlineData("predecessor-replace", CheckStatus.Fail)]
    [InlineData("older-edge-mismatch", CheckStatus.Pass)]
    [InlineData("matches-oldest-not-direct", CheckStatus.Fail)]
    [InlineData("historical-bytes", CheckStatus.Fail)]
    [InlineData("current-tail", CheckStatus.NotEvaluated)]
    [InlineData("previous-tail", CheckStatus.NotEvaluated)]
    [InlineData("study-id-truncated", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-study-id", CheckStatus.NotEvaluated)]
    [InlineData("foreign-study-id", CheckStatus.NotEvaluated)]
    [InlineData("char-reference", CheckStatus.Pass)]
    [InlineData("two-owners-one-match", CheckStatus.Fail)]
    [InlineData("known-plus-unknown", CheckStatus.Fail)]
    [InlineData("unknown-plus-known", CheckStatus.Fail)]
    public async Task DirectPredecessorAndTextCompletenessAreIndependent(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("A", "B", scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(scenario == "two-owners-one-match" ? "second-owner" : "stf", finding.Location.LeafId);
            Assert.Contains(finding.Location.Files!, file => file.SourceId == fixture.Packages.Sequences["0001"].SourceId && file.LogicalPath.StartsWith("0001/", StringComparison.Ordinal));
            if (scenario == "historical-bytes") Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0001/m4/study/second.xml" && file.SourceId == fixture.Packages.Sequences["0001"].SourceId);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("default-operation")]
    [InlineData("modified-conflict")]
    [InlineData("modified-missing")]
    [InlineData("modified-default")]
    [InlineData("duplicate-previous")]
    [InlineData("previous-hash")]
    [InlineData("previous-context")]
    [InlineData("broken-study-id-chain")]
    public async Task ContradictoryFrozenFactsCannotEstablishContinuity(string scenario)
    {
        var fixture = await Fixture("A", "B");
        var xml = fixture.Lifecycle.XmlInspections.Select(observation =>
        {
            var current = observation.InputDigest == fixture.Packages.Target.InputDigest;
            var index = Assert.Single(observation.Backbones, document => document.Kind == BackboneKind.Ich);
            var indexElements = index.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
                element.NamespaceUri, element.Attributes.Values.Where(attribute => !(current && scenario == "modified-missing" && element.Attribute("ID") == "stf" && attribute.Name == "modified-file"))
                    .Select(attribute => current && element.Attribute("ID") == "stf" ?
                    scenario == "default-operation" && attribute.Name == "operation" ? attribute with { IsDefault = true, ExplicitValue = null } :
                    scenario == "modified-default" && attribute.Name == "modified-file" ? attribute with { IsDefault = true, ExplicitValue = null } :
                    scenario == "modified-conflict" && attribute.Name == "modified-file" ? attribute with { Value = "../0001/index.xml#different", ExplicitValue = "../0001/index.xml#different" } : attribute : attribute),
                element.Children, element.Text, element.Location));
            var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
                index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, indexElements, index.Nodes, index.Leaves,
                index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
            var documents = new List<ParsedStudyTaggingFile>();
            foreach (var document in observation.StudyTaggingFiles.Documents)
            {
                var id = Assert.Single(document.Elements, element => element.LocalName == "study-id");
                var elements = document.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
                    element.NamespaceUri, element.Attributes.Values, current && scenario == "broken-study-id-chain" && element.NodePath == id.ParentPath ?
                        element.Children.Where(path => path != id.NodePath) : element.Children, element.Text, element.Location));
                var changed = new ParsedStudyTaggingFile(document.LogicalPath, !current && scenario == "previous-hash" ? new string('f', 64) : document.CapturedSha256,
                    document.ReadComplete, document.DtdStatus, document.SystemIdentifier, elements, document.ProcessingInstructions, document.ResolvedAssets);
                documents.Add(changed);
                if (!current && scenario == "duplicate-previous") documents.Add(changed);
            }
            return new PackageXmlInspection(observation.InputDigest, observation.ProfileSnapshotId, observation.AssetManifestDigest, observation.LimitsDigest,
                observation.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changedIndex), observation.Checks,
                !current && scenario == "previous-context" ? new string('f', 64) : observation.RulesDigest,
                new(observation.StudyTaggingFiles.InventoryComplete, documents));
        });
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, xml,
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var result = FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task UnlistedReverseStudyIdCannotEstablishUniqueIdentity()
    {
        var fixture = await Fixture("A", "A");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections, xml => xml.InputDigest == fixture.Packages.Target.InputDigest);
        var stf = Assert.Single(original.StudyTaggingFiles.Documents);
        var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
        var id = Assert.Single(stf.Elements, element => element.LocalName == "study-id");
        var extraPath = identifier.NodePath + "/study-id[2]";
        var extra = new ParsedXmlElement(extraPath, identifier.NodePath, "study-id", "study-id", "", [], [], "B",
            id.Location with { NodePath = extraPath });
        var changedStf = new ParsedStudyTaggingFile(stf.LogicalPath, stf.CapturedSha256, stf.ReadComplete, stf.DtdStatus,
            stf.SystemIdentifier, stf.Elements.Append(extra), stf.ProcessingInstructions, stf.ResolvedAssets);
        Assert.Equal(2, changedStf.Elements.Count(element => element.ParentPath == identifier.NodePath && element.LocalName == "study-id"));
        Assert.DoesNotContain(extraPath, identifier.Children);
        var changed = new PackageXmlInspection(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest,
            original.LimitsDigest, original.Backbones, original.Checks, original.RulesDigest,
            new(original.StudyTaggingFiles.InventoryComplete, [changedStf]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId,
            fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != original.InputDigest).Append(changed),
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.True(PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default).TargetScopeComplete);
        var result = FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task DerivedDeleteCannotHideAnExplicitAppendOwner()
    {
        var fixture = await Fixture("A", "B");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections, xml => xml.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.Ich);
        Assert.Equal("append", Assert.Single(index.Elements, element => element.Attribute("ID") == "stf").Attribute("operation"));
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id == "stf" ? leaf with { Operation = "delete" } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var changed = new PackageXmlInspection(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest,
            original.LimitsDigest, original.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changedIndex),
            original.Checks, original.RulesDigest, original.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId,
            fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != original.InputDigest).Append(changed),
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var bindings = PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default);
        Assert.True(bindings.TargetScopeComplete);
        Assert.Empty(Assert.Single(bindings.Documents, document => document.IsTarget).Owners);
        var result = FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExplicitModifiedCdataDoesNotRequireAuxiliaryRawCopy()
    {
        var fixture = await Fixture("A", "B");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections, xml => xml.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.Ich);
        var elements = index.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
            element.NamespaceUri, element.Attributes.Values.Select(attribute => attribute.Name == "modified-file" ? attribute with { ExplicitValue = null } : attribute),
            element.Children, element.Text, element.Location));
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes, index.Leaves,
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var changed = new PackageXmlInspection(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest, original.LimitsDigest,
            original.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changedIndex), original.Checks, original.RulesDigest, original.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId,
            fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != original.InputDigest).Append(changed), fixture.Lifecycle.Events,
            fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var result = FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task KnownNonStfOperationConflictCannotPolluteMatchingStfEdge()
    {
        var fixture = await Fixture("A", "A", "ordinary-xml");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections, xml => xml.InputDigest == fixture.Packages.Target.InputDigest);
        Assert.True(original.StudyTaggingFiles.InventoryComplete);
        Assert.DoesNotContain(original.StudyTaggingFiles.Documents, document => document.LogicalPath == "0002/m4/study/ordinary.xml");
        Assert.Contains(fixture.Packages.Target.Manifest.Files, file => file.LogicalPath == "0002/m4/study/ordinary.xml");
        var index = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.Ich);
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id == "ordinary" ? leaf with { Operation = "delete" } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var changed = new PackageXmlInspection(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest,
            original.LimitsDigest, original.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changedIndex),
            original.Checks, original.RulesDigest, original.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId,
            fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != original.InputDigest).Append(changed),
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.True(PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default).TargetScopeComplete);
        var result = FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task EachAppendEdgeSharesBudgetAndImmutableSnapshots(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("A", "B", "two-owners");
        var snapshots = new List<RuleCheckResult>();
        var result = FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); });
        Assert.Equal(expected, result!.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownMismatch(bool afterFailure)
    {
        var fixture = await Fixture("A", "B");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceAndSelectionDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("A", "B", variant: variant, date: date)).Status);

    [Theory]
    [InlineData("A", CheckStatus.Pass)]
    [InlineData("B", CheckStatus.Fail)]
    public async Task DeliveryAndReportExposeDirectEdgeResult(string current, CheckStatus expected)
    {
        var fixture = await Fixture("A", current);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1850").Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == "FDA47-1850").CheckStatus);
    }

    [Fact]
    public async Task OldCatalogCannotBorrowCurrentRule()
    {
        var fixture = await Fixture("A", "B");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1850"));
        Assert.Null(FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfStudyIdContinuityRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string previous, string current, string scenario = "valid", string variant = "valid", string? date = "2026-11-20")
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
        if (scenario is "older-edge-mismatch" or "matches-oldest-not-direct") { previous = "B"; current = scenario == "older-edge-mismatch" ? "B" : "A"; }
        if (scenario == "char-reference") previous = current = "A1";
        if (scenario == "two-owners-one-match") current = "A";
        var target = new Capture(app, "0002", current, scenario, true);
        var history = new List<ICapturedPackageInput>();
        if (scenario != "missing-history") history.Add(new Capture(app, "0001", previous, scenario, false));
        if (scenario is "older-edge-mismatch" or "matches-oldest-not-direct") history.Add(new Capture(app, "0000", "A", "valid", false));
        var packages = new PackageInputSet(target, new(app, "0002", history.Select(capture => new HistoryBaselineEntry(capture.Manifest.SequenceNumber,
            HistorySourceKind.ImportedExternal, capture.SourceId, capture.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified))), history);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
            foreach (var stf in xml.StudyTaggingFiles.Documents)
            {
                var currentDocument = xml.InputDigest == target.InputDigest;
                if (currentDocument && scenario is "duplicate-study-id" or "foreign-study-id")
                {
                    Assert.True(stf.ReadComplete);
                    var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
                    var ids = stf.Elements.Where(element => element.ParentPath == identifier.NodePath && element.LocalName == "study-id").ToArray();
                    Assert.Equal(scenario == "duplicate-study-id" ? 2 : 1, ids.Length);
                    if (scenario == "foreign-study-id") Assert.Equal("urn:foreign", ids[0].NamespaceUri);
                    continue;
                }
                if (currentDocument && scenario is "current-tail" or "study-id-truncated" || !currentDocument && scenario == "previous-tail")
                {
                    Assert.False(stf.ReadComplete);
                    var id = Assert.Single(stf.Elements, element => element.LocalName == "study-id");
                    if (scenario != "study-id-truncated") Assert.Equal(currentDocument ? current : previous, id.Text);
                    continue;
                }
                Assert.True(stf.ReadComplete);
                Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
            }
        }
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, default);
        if (scenario is "earlier-history-missing" or "predecessor-replace")
            Assert.Contains(bindings.Documents.SelectMany(document => document.Owners), owner => owner.Qualified && owner.Leaf.Address!.SequenceNumber == "0001" && owner.Leaf.Operation == (scenario == "predecessor-replace" ? "replace" : "append"));
        if (scenario == "historical-bytes") Assert.Contains(Assert.Single(bindings.Documents, document => document.Document.LogicalPath == "0001/m4/study/second.xml").Owners,
            owner => owner.Qualified && owner.Leaf.Address!.SequenceNumber == "0002");
        if (scenario == "char-reference") Assert.Equal("A1", Assert.Single(Assert.Single(bindings.Documents, document => document.IsTarget).Document.Elements, element => element.LocalName == "study-id").Text);
        if (scenario == "non-stf-predecessor")
        {
            var previousXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == history[0].InputDigest);
            Assert.True(previousXml.StudyTaggingFiles.InventoryComplete);
            Assert.DoesNotContain(bindings.Documents, document => document.Document.LogicalPath == "0001/m4/study/stf.xml");
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string studyId, string scenario, bool current)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var section = new XElement("m4-3-literature-references");
            var operation = current ? scenario is "new" or "replace" or "delete" ? scenario : "append" :
                scenario is "earlier-history-missing" or "older-edge-mismatch" or "matches-oldest-not-direct" ? "append" : scenario == "predecessor-replace" ? "replace" : "new";
            var modified = current ? scenario switch { "missing-modified" => null, "modified-query" => "../0001/index.xml?q=1#stf", "future-predecessor" => "../0003/index.xml#stf",
                "missing-predecessor" => "../0001/index.xml#absent", _ => "../0001/index.xml#stf" } : "../0000/index.xml#stf";
            var owner = Leaf("stf", current && scenario == "historical-bytes" ? "../0001/m4/study/second.xml" : "m4/study/stf.xml", operation, modified);
            section.Add(owner);
            if (current && scenario == "ordinary-xml") section.Add(Leaf("ordinary", "m4/study/ordinary.xml", "append", "../0001/index.xml#ordinary"));
            if (current && scenario is "two-owners" or "two-owners-one-match" or "known-plus-unknown" or "unknown-plus-known")
            {
                var second = Leaf("second-owner", "m4/study/stf.xml", "append", scenario is "known-plus-unknown" or "unknown-plus-known" ? "../0000/index.xml#absent" : "../0001/index.xml#second");
                if (scenario == "unknown-plus-known") section.AddFirst(second);
                else section.Add(second);
            }
            if (!current && scenario is "historical-bytes" or "two-owners" or "two-owners-one-match") section.Add(Leaf("second", "m4/study/second.xml", "new", null));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml", "new", null)),
                new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            var stf = Stf(studyId);
            if (current && scenario == "char-reference") stf = stf.Replace("<study-id>A1</study-id>", "<study-id>A&#49;</study-id>", StringComparison.Ordinal);
            if (current && scenario == "duplicate-study-id") stf = stf.Replace("</study-id>", "</study-id><study-id>B</study-id>", StringComparison.Ordinal);
            if (current && scenario == "foreign-study-id") stf = stf.Replace("<study-id>", "<f:study-id xmlns:f='urn:foreign'>", StringComparison.Ordinal).Replace("</study-id>", "</f:study-id>", StringComparison.Ordinal);
            if (current && scenario == "current-tail" || !current && scenario == "previous-tail") stf = stf[..^8];
            if (current && scenario == "study-id-truncated") stf = stf[..(stf.IndexOf("<study-id>", StringComparison.Ordinal) + 11)];
            if (!current && scenario == "non-stf-predecessor") stf = "<other/>";
            if (!(current && scenario == "historical-bytes")) _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
            if (current && scenario == "ordinary-xml") _files[sequence + "/m4/study/ordinary.xml"] = "<other/>"u8.ToArray();
            if (!current && scenario is "historical-bytes" or "two-owners" or "two-owners-one-match")
                _files[sequence + "/m4/study/second.xml"] = Encoding.UTF8.GetBytes(Stf(scenario == "historical-bytes" ? "B" : "C"));
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Stf(string studyId) => "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" +
            "<study-identifier><title>Study</title><study-id>" + System.Security.SecurityElement.Escape(studyId) + "</study-id></study-identifier><study-document/></ectd:study>";
        private static XElement Leaf(string id, string href, string operation, string? modified)
        {
            var leaf = new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", operation == "delete" ? "" : new string('0', 32)),
                new XAttribute("checksum-type", "md5"), new XAttribute("version", "STF Version 2.2"), new XElement("title", id));
            if (operation != "delete") leaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), href);
            if (operation != "new" && modified is not null) leaf.SetAttributeValue("modified-file", modified);
            return leaf;
        }
        public bool Armed { get; set; }
        public string SourceId => "fda1850-" + Manifest.SequenceNumber;
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
