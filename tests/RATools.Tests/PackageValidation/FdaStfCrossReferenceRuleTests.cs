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

public sealed class FdaStfCrossReferenceRuleTests
{
    [Theory]
    [InlineData("another-stf", CheckStatus.Fail, 1)]
    [InlineData("orphan-source", CheckStatus.Fail, 1)]
    [InlineData("same-study", CheckStatus.Fail, 1)]
    [InlineData("ordinary-xml", CheckStatus.Pass, 0)]
    [InlineData("nonxml-unprobed", CheckStatus.NotEvaluated, 0)]
    [InlineData("no-references", CheckStatus.Pass, 0)]
    [InlineData("no-stf", CheckStatus.NotApplicable, 0)]
    [InlineData("direct-file", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing-file", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing-leaf", CheckStatus.NotEvaluated, 0)]
    [InlineData("unknown-target", CheckStatus.NotEvaluated, 0)]
    [InlineData("wrong-target-root", CheckStatus.NotEvaluated, 0)]
    [InlineData("source-tail", CheckStatus.Fail, 1)]
    [InlineData("target-tail", CheckStatus.Fail, 1)]
    [InlineData("index-tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("unknown-second", CheckStatus.Fail, 1)]
    [InlineData("two-references", CheckStatus.Fail, 2)]
    [InlineData("self", CheckStatus.NotEvaluated, 0)]
    [InlineData("alias-self", CheckStatus.NotEvaluated, 0)]
    [InlineData("historical-target", CheckStatus.Fail, 1)]
    [InlineData("historical-leaf", CheckStatus.Fail, 1)]
    [InlineData("historical-source", CheckStatus.Fail, 1)]
    [InlineData("historical-only", CheckStatus.NotApplicable, 0)]
    [InlineData("missing-history", CheckStatus.NotEvaluated, 0)]
    [InlineData("historical-owner-fragment", CheckStatus.NotEvaluated, 0)]
    public async Task RequiresExactLeafToActualStfIdentity(string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        foreach (var finding in check.Findings)
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("doc-content/@xlink:href", finding.Location.FieldPath);
            var sourceSequence = scenario == "historical-source" ? "0000" : "0001";
            var targetSequence = scenario is "historical-source" or "historical-target" or "historical-leaf" ? "0000" : "0001";
            Assert.Equal(sourceSequence + "/m4/study/stf-a.xml", finding.Location.LogicalPath);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == targetSequence + "/m4/study/b.xml" && file.SourceId == fixture.Packages.Sequences[targetSequence].SourceId);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == finding.Location.LogicalPath && file.SourceId == fixture.Packages.Sequences[sourceSequence].SourceId);
        }
    }

    [Theory]
    [InlineData("duplicate-source")]
    [InlineData("duplicate-target")]
    [InlineData("target-hash")]
    [InlineData("source-hash")]
    [InlineData("context")]
    public async Task AmbiguousOrMismatchedFrozenFactsCannotEstablishForbiddenReference(string scenario)
    {
        var fixture = await Fixture("another-stf");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var documents = new List<ParsedStudyTaggingFile>();
        foreach (var stf in xml.StudyTaggingFiles.Documents)
        {
            var source = stf.LogicalPath.EndsWith("stf-a.xml", StringComparison.Ordinal);
            var changed = new ParsedStudyTaggingFile(stf.LogicalPath,
                scenario == "source-hash" && source || scenario == "target-hash" && !source ? new string('f', 64) : stf.CapturedSha256,
                stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
            documents.Add(changed);
            if (scenario == "duplicate-source" && source || scenario == "duplicate-target" && !source) documents.Add(changed);
        }
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, scenario == "context" ? new string('f', 64) : xml.RulesDigest, new(xml.StudyTaggingFiles.InventoryComplete, documents));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var check = FdaStfCrossReferenceRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task FindingBudgetAndSnapshotsArePreserved(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-references");
        var snapshots = new List<RuleCheckResult>();
        var check = FdaStfCrossReferenceRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.Equal(expected, check!.Status);
        Assert.Equal(budget, check.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData("leaf-href-conflict", CheckStatus.NotEvaluated)]
    [InlineData("broken-reference-chain", CheckStatus.NotEvaluated)]
    [InlineData("broken-and-valid", CheckStatus.Fail)]
    public async Task ContradictoryReferenceFactsDoNotBecomeDefiniteResults(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario == "leaf-href-conflict" ? "self" : scenario == "broken-and-valid" ? "two-references" : "another-stf");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => scenario == "leaf-href-conflict" && leaf.Id == "a" ? leaf with { Href = "m4/study/b.xml" } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var source = Assert.Single(xml.StudyTaggingFiles.Documents, document => document.LogicalPath.EndsWith("stf-a.xml", StringComparison.Ordinal));
        var firstReference = source.Elements.First(element => element.LocalName == "doc-content");
        var sourceElements = source.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values,
            scenario != "leaf-href-conflict" && element.NodePath == firstReference.ParentPath ? element.Children.Where(path => path != firstReference.NodePath) : element.Children,
            element.Text, element.Location));
        var changedSource = new ParsedStudyTaggingFile(source.LogicalPath, source.CapturedSha256, source.ReadComplete, source.DtdStatus,
            source.SystemIdentifier, sourceElements, source.ProcessingInstructions, source.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changedIndex), xml.Checks, xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, xml.StudyTaggingFiles.Documents.Where(document => document.LogicalPath != source.LogicalPath).Append(changedSource)));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var binding = PackageStfBindingReader.Read(fixture.Packages, lifecycle, fixture.Context, default);
        Assert.True(binding.TargetScopeComplete);
        var boundSource = Assert.Single(binding.Documents, document => document.Document.LogicalPath == source.LogicalPath);
        if (scenario == "leaf-href-conflict")
        {
            Assert.Equal(StfReferenceStatus.Resolved, Assert.Single(boundSource.References).Status);
            Assert.Equal("m4/study/stf-a.xml", Assert.Single(index.Elements, element => element.Attribute("ID") == "a").Attribute("href", "http://www.w3c.org/1999/xlink"));
            Assert.Equal("m4/study/b.xml", Assert.Single(boundSource.References).Leaf!.Href);
        }
        else
        {
            Assert.False(boundSource.StructureComplete);
            Assert.Equal(scenario == "broken-and-valid" ? 1 : 0, boundSource.References.Count);
        }
        var check = FdaStfCrossReferenceRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Single(check.Findings);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("another-stf");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfCrossReferenceRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("another-stf", variant, date)).Status);

    [Theory]
    [InlineData("another-stf", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("ordinary-xml", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    public async Task ReportRetainsPartialInterpretationBoundary(string scenario, CheckStatus component, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(component, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1799").Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1799").CheckStatus);
    }

    [Fact]
    public async Task OldCatalogCannotBorrowCurrentRule()
    {
        var fixture = await Fixture("another-stf");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1799"));
        Assert.Null(FdaStfCrossReferenceRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfCrossReferenceRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    internal sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    internal static async Task<FixtureData> Fixture(string scenario, string variant = "valid", string? date = "2026-11-20")
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
        var history = scenario is "historical-source" or "historical-target" or "historical-leaf" or "historical-only" or "historical-owner-fragment" ? new Capture(app, "0000", "another-stf") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
            if (scenario == "index-tail" && xml.InputDigest == target.InputDigest) Assert.False(index.ReadComplete);
            else Assert.Equal(CheckStatus.Pass, index.DtdStatus);
            foreach (var document in xml.StudyTaggingFiles.Documents)
            {
                if (document.LogicalPath.EndsWith("b.xml", StringComparison.Ordinal) && scenario is "unknown-target" or "wrong-target-root") continue;
                Assert.Contains("stf-dtd", document.ResolvedAssets);
                Assert.Single(document.Elements, element => element.ParentPath is null && element.LocalName == "study");
                if (scenario is not ("source-tail" or "target-tail")) Assert.Equal(CheckStatus.Pass, document.DtdStatus);
            }
        }
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, default);
        var targetXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == target.InputDigest);
        if (scenario == "ordinary-xml")
        {
            Assert.True(targetXml.StudyTaggingFiles.InventoryComplete);
            Assert.Contains(target.Manifest.Files, file => file.LogicalPath == "0001/m4/study/b.xml");
            Assert.DoesNotContain(targetXml.StudyTaggingFiles.Documents, document => document.LogicalPath == "0001/m4/study/b.xml");
        }
        if (scenario is "source-tail" or "target-tail") Assert.False(Assert.Single(targetXml.StudyTaggingFiles.Documents,
            document => document.LogicalPath.EndsWith(scenario == "source-tail" ? "stf-a.xml" : "b.xml", StringComparison.Ordinal)).ReadComplete);
        if (scenario == "historical-leaf")
        {
            var reference = Assert.Single(Assert.Single(bindings.Documents, document => document.Document.LogicalPath == "0001/m4/study/stf-a.xml").References);
            Assert.Equal(StfReferenceStatus.Resolved, reference.Status);
            Assert.Equal("0000", reference.Target!.SequenceNumber);
        }
        if (scenario is "another-stf" or "orphan-source" or "same-study" or "source-tail" or "target-tail")
        {
            var source = Assert.Single(bindings.Documents, document => document.Document.LogicalPath.EndsWith("stf-a.xml", StringComparison.Ordinal));
            Assert.Equal(StfReferenceStatus.Resolved, Assert.Single(source.References).Status);
            if (scenario == "orphan-source") Assert.Empty(source.Owners);
        }
        if (scenario == "historical-source") Assert.Contains(Assert.Single(bindings.Documents, document => document.Document.LogicalPath == "0000/m4/study/stf-a.xml").Owners,
            owner => owner.Qualified && owner.Leaf.Address!.SequenceNumber == "0001");
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
            var section = new XElement("m4-3-literature-references");
            var oldSource = scenario is "historical-source" or "historical-owner-fragment";
            if (scenario is not ("orphan-source" or "historical-only" or "no-stf")) section.Add(Leaf("a", oldSource ?
                "../0000/m4/study/stf-a.xml" + (scenario == "historical-owner-fragment" ? "#invalid" : "") : "m4/study/stf-a.xml"));
            var bPath = scenario is "historical-target" or "historical-leaf" or "historical-source" or "historical-owner-fragment" or "missing-history" ? "../0000/m4/study/b.xml" :
                scenario == "alias-self" ? "m4/study/stf-a.xml" : scenario == "nonxml-unprobed" ? "m4/study/b.pdf" : "m4/study/b.xml";
            if (scenario != "historical-only" && scenario != "missing-leaf") section.Add(Leaf("b", bPath));
            if (scenario == "two-references") section.Add(Leaf("b-alias", bPath));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var reference = scenario == "self" ? "../../index.xml#a" : scenario == "direct-file" ? "b.xml" :
                scenario == "historical-leaf" ? "../../../0000/index.xml#b" : "../../index.xml#b";
            var content = scenario == "no-references" ? "" : "<doc-content xlink:href='" + reference + "'/>";
            if (scenario == "unknown-second") content += "<doc-content xlink:href='../../../0000/index.xml#missing'/>";
            if (scenario == "two-references") content += "<doc-content xlink:href='../../index.xml#b-alias'/>";
            var source = Stf("A", content);
            var targetStf = Stf(scenario == "same-study" ? "A" : "B", "");
            if (scenario == "source-tail") source = source[..^8];
            if (scenario == "target-tail") targetStf = targetStf[..^8];
            if (scenario == "ordinary-xml" || scenario == "no-stf") targetStf = "<other/>";
            if (scenario == "unknown-target") targetStf = "<unknown";
            if (scenario == "wrong-target-root") targetStf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><other/>";
            if (!oldSource && scenario is not ("historical-only" or "no-stf")) _files[sequence + "/m4/study/stf-a.xml"] = Encoding.UTF8.GetBytes(source);
            if (scenario is not ("historical-target" or "historical-leaf" or "historical-source" or "historical-owner-fragment" or "historical-only" or "missing-history" or "missing-file" or "alias-self" or "nonxml-unprobed"))
                _files[sequence + "/m4/study/b.xml"] = Encoding.UTF8.GetBytes(targetStf);
            if (scenario == "nonxml-unprobed") _files[sequence + "/m4/study/b.pdf"] = "%PDF-1.4 example"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Stf(string id, string content) => "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" +
            "<study-identifier><title>Study</title><study-id>" + id + "</study-id></study-identifier><study-document>" + content + "</study-document></ectd:study>";
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)),
            new XAttribute("checksum-type", "md5"), new XAttribute("version", "STF Version 2.2"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1799-" + Manifest.SequenceNumber;
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
