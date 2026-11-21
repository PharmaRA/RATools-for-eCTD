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

public sealed class FdaStfFileExtensionRuleTests
{
    private const string RuleId = "FDA47-1867";

    [Theory]
    [InlineData("stf-a.xml", "valid", CheckStatus.Pass)]
    [InlineData("other.xml", "valid", CheckStatus.Pass)]
    [InlineData("stf-a.XML", "valid", CheckStatus.Fail)]
    [InlineData("stf-a.Xml", "valid", CheckStatus.Fail)]
    [InlineData("stf-a.txt", "valid", CheckStatus.Fail)]
    [InlineData("other.pdf", "valid", CheckStatus.Fail)]
    [InlineData("study", "valid", CheckStatus.Fail)]
    [InlineData("study.xml.bak", "valid", CheckStatus.Fail)]
    [InlineData("other.txt", "unreferenced", CheckStatus.Fail)]
    [InlineData("other.xml", "unreferenced", CheckStatus.Pass)]
    [InlineData("other.txt", "tail", CheckStatus.Fail)]
    [InlineData("other.xml", "tail", CheckStatus.NotEvaluated)]
    [InlineData("other.txt", "wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("other.txt", "missing-root", CheckStatus.NotEvaluated)]
    [InlineData("ordinary.pdf", "ordinary", CheckStatus.NotApplicable)]
    [InlineData("stf-a.pdf", "ordinary", CheckStatus.NotApplicable)]
    [InlineData("other.txt", "history", CheckStatus.Fail)]
    [InlineData("other.xml", "history", CheckStatus.Pass)]
    [InlineData("other.txt", "history-tail", CheckStatus.Fail)]
    [InlineData("other.txt", "history-only", CheckStatus.NotApplicable)]
    [InlineData("other.txt", "history-missing", CheckStatus.NotEvaluated)]
    [InlineData("other.txt", "history-fragment", CheckStatus.NotEvaluated)]
    [InlineData("other.txt", "missing", CheckStatus.NotEvaluated)]
    public async Task ActualIdentityAndExtensionRemainSeparate(string name, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(name, scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal(".xml", finding.Expected);
            Assert.Equal((scenario is "history" or "history-tail" ? "0000" : "0001") + "/m4/study/" + name, finding.Location.LogicalPath);
            Assert.Equal(finding.Location.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("context")]
    [InlineData("duplicate")]
    public async Task FrozenFactsCannotBorrowIdentity(string scenario)
    {
        var fixture = await Fixture("other.txt");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var original = Assert.Single(xml.StudyTaggingFiles.Documents);
        var document = new ParsedStudyTaggingFile(original.LogicalPath, scenario == "hash" ? new string('f', 64) : original.CapturedSha256,
            original.ReadComplete, original.DtdStatus, original.SystemIdentifier, original.Elements, original.ProcessingInstructions, original.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, scenario == "context" ? new string('f', 64) : xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, scenario == "duplicate" ? [document, document] : [document]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.Equal(CheckStatus.NotEvaluated, Inspect(fixture with { Lifecycle = lifecycle })!.Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("profile")]
    public async Task HistoricalNonXmlIdentityMismatchCannotBecomeNotApplicable(string scenario)
    {
        var fixture = await Fixture("other.txt", "history");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections, xml => xml.InputDigest != fixture.Packages.Target.InputDigest);
        var stf = Assert.Single(original.StudyTaggingFiles.Documents);
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, scenario == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        var changed = new PackageXmlInspection(original.InputDigest, scenario == "profile" ? PackageValidationCatalog.IchProfile : original.ProfileSnapshotId,
            original.AssetManifestDigest, original.LimitsDigest, original.Backbones, original.Checks, original.RulesDigest, new(true, [document]));
        fixture = ReplaceXml(fixture, changed);
        var bindings = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.True(bindings.TargetScopeComplete);
        Assert.DoesNotContain(bindings.Documents, bound => bound.Document.LogicalPath == stf.LogicalPath);
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(false, "replace", CheckStatus.Pass)]
    [InlineData(true, "delete", CheckStatus.NotEvaluated)]
    public async Task DerivedOwnerConflictsRespectIndependentCurrentIdentity(bool historical, string operation, CheckStatus expected)
    {
        var fixture = await Fixture(historical ? "other.txt" : "other.xml", historical ? "history" : "valid");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections, xml => xml.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.Ich);
        Assert.Equal("new", Assert.Single(index.Elements, element => element.Attribute("ID") == "owner").Attribute("operation"));
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id == "owner" ? leaf with { Operation = operation } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        var changed = new PackageXmlInspection(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest,
            original.LimitsDigest, original.Backbones.Where(backbone => backbone.Kind != BackboneKind.Ich).Append(changedIndex),
            original.Checks, original.RulesDigest, original.StudyTaggingFiles);
        fixture = ReplaceXml(fixture, changed);
        Assert.True(PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default).TargetScopeComplete);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Findings);
    }

    private static FixtureData ReplaceXml(FixtureData fixture, PackageXmlInspection changed) => fixture with
    {
        Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId,
            fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != changed.InputDigest).Append(changed),
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest)
    };

    [Theory]
    [InlineData("default")]
    [InlineData("missing")]
    [InlineData("unknown")]
    public async Task CurrentKnownStfDoesNotRequireOwnerOperationEvidence(string variant)
    {
        var fixture = await Fixture("other.xml");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(original.Backbones, backbone => backbone.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Elements, element => element.Attribute("ID") == "owner");
        var alteredOwner = new ParsedXmlElement(owner.NodePath, owner.ParentPath, owner.Name, owner.LocalName, owner.NamespaceUri,
            owner.Attributes.Values.Where(attribute => variant != "missing" || attribute.Name != "operation")
                .Select(attribute => attribute.Name != "operation" ? attribute : variant == "default" ?
                    attribute with { IsDefault = true, ExplicitValue = null } : attribute with { Value = "unknown", ExplicitValue = "unknown" }),
            owner.Children, owner.Text, owner.Location);
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements.Select(element => element.NodePath == owner.NodePath ? alteredOwner : element),
            index.Nodes, index.Leaves, index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        fixture = ReplaceXml(fixture, new(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest, original.LimitsDigest,
            original.Backbones.Where(backbone => backbone.Kind != BackboneKind.Ich).Append(changedIndex), original.Checks, original.RulesDigest, original.StudyTaggingFiles));
        var bindings = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.True(bindings.TargetScopeComplete);
        Assert.True(Assert.Single(bindings.Documents).IsTarget);
        Assert.False(Assert.Single(Assert.Single(bindings.Documents).Owners).Qualified);
        Assert.Equal(CheckStatus.Pass, Inspect(fixture)!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-21", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-21", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("other.txt", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task FindingsShareBudgetAndImmutableSnapshots(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("other.txt", "two");
        var snapshots = new List<RuleCheckResult>();
        var check = Inspect(fixture, budget, progress: result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); })!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(budget, check.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownViolation(bool afterFailure)
    {
        var fixture = await Fixture("other.txt");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token, progress: result =>
        { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancel.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("other.xml", CheckStatus.Pass)]
    [InlineData("other.txt", CheckStatus.Fail)]
    public async Task DeliveryAndReportExposeExtensionCheck(string name, CheckStatus expected)
    {
        var fixture = await Fixture(name);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("other.txt");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfFileExtensionRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);

    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string name, string scenario = "valid", string variant = "valid", string? date = "2026-11-21")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", name, scenario);
        var history = scenario is "history" or "history-only" or "history-fragment" or "history-tail" ? new Capture(app, "0000", name, scenario == "history-tail" ? "tail" : "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
        if (scenario is "valid" or "unreferenced" or "two")
        { Assert.True(xml.StudyTaggingFiles.InventoryComplete); Assert.All(xml.StudyTaggingFiles.Documents, document => Assert.Equal(CheckStatus.Pass, document.DtdStatus)); Assert.NotEmpty(xml.StudyTaggingFiles.Documents); }
        if (history is not null)
        {
            var oldXml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == history.InputDigest);
            var historicalStf = Assert.Single(oldXml.StudyTaggingFiles.Documents);
            if (scenario == "history-tail")
            {
                Assert.False(historicalStf.ReadComplete);
                Assert.Equal("study", Assert.Single(historicalStf.Elements, element => element.ParentPath is null).LocalName);
            }
            else
            {
                Assert.True(oldXml.StudyTaggingFiles.InventoryComplete);
                Assert.Equal(CheckStatus.Pass, historicalStf.DtdStatus);
            }
        }
        if (scenario == "tail")
        {
            var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
            Assert.False(stf.ReadComplete);
            Assert.Equal("study", Assert.Single(stf.Elements, element => element.ParentPath is null).LocalName);
        }
        if (scenario is "wrong-root" or "missing-root")
        {
            Assert.False(xml.StudyTaggingFiles.InventoryComplete);
            Assert.DoesNotContain(Assert.Single(xml.StudyTaggingFiles.Documents).Elements,
                element => element.ParentPath is null && element.LocalName == "study" && element.NamespaceUri == "http://www.ich.org/ectd");
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string name, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var section = new XElement("m4-3-literature-references");
            if (scenario is not ("unreferenced" or "history-only"))
                section.Add(Leaf("owner", (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name + (scenario == "history-fragment" ? "#unexpected" : "")));
            section.Add(Leaf("report", "m4/study/report.pdf"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document/></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "missing-root") stf = declaration;
            if (scenario == "ordinary") stf = "%PDF-1.4 ordinary";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "two") _files[sequence + "/m4/study/second.txt"] = Encoding.UTF8.GetBytes(stf);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1867-" + Manifest.SequenceNumber;
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
