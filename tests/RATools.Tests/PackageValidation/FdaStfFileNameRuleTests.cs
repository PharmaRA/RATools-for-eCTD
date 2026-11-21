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

public sealed class FdaStfFileNameRuleTests
{
    private const string RuleId = "FDA47-1884";

    [Theory]
    [InlineData("stf-A.xml", "A", "valid", CheckStatus.Pass)]
    [InlineData("stf-a.xml", "A", "valid", CheckStatus.Fail)]
    [InlineData("STF-A.xml", "A", "valid", CheckStatus.Fail)]
    [InlineData("study.xml", "A", "valid", CheckStatus.Fail)]
    [InlineData("stf-B.xml", "A", "valid", CheckStatus.Fail)]
    [InlineData("stf-A.txt", "A", "valid", CheckStatus.Fail)]
    [InlineData("stf-A.XML", "A", "valid", CheckStatus.Fail)]
    [InlineData("stf-A", "A", "valid", CheckStatus.Fail)]
    [InlineData("stf-A.xml.bak", "A", "valid", CheckStatus.Fail)]
    [InlineData("stf-A.B.xml", "A.B", "valid", CheckStatus.Pass)]
    [InlineData("stf-A.xml", " A ", "valid", CheckStatus.Fail)]
    [InlineData("stf-A.xml", "A", "char-reference", CheckStatus.Pass)]
    [InlineData("stf-A.xml", "A", "unreferenced", CheckStatus.Pass)]
    [InlineData("wrong.xml", "A", "unreferenced", CheckStatus.Fail)]
    [InlineData("stf-A.xml", "", "valid", CheckStatus.NotEvaluated)]
    [InlineData("wrong.xml", "", "valid", CheckStatus.Fail)]
    [InlineData("stf-A.xml", "A", "tail", CheckStatus.NotEvaluated)]
    [InlineData("wrong.xml", "A", "tail", CheckStatus.Fail)]
    [InlineData("stf-A.xml", "A", "missing-id", CheckStatus.NotEvaluated)]
    [InlineData("wrong.xml", "A", "wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong.xml", "A", "missing-root", CheckStatus.NotEvaluated)]
    [InlineData("stf-A.pdf", "A", "ordinary", CheckStatus.NotApplicable)]
    [InlineData("stf-A.xml", "A", "history", CheckStatus.Pass)]
    [InlineData("wrong.xml", "A", "history", CheckStatus.Fail)]
    [InlineData("wrong.xml", "A", "history-only", CheckStatus.NotApplicable)]
    [InlineData("stf-A.xml", "A", "history-missing", CheckStatus.NotEvaluated)]
    [InlineData("stf-A.xml", "A", "history-fragment", CheckStatus.NotEvaluated)]
    [InlineData("wrong.xml", "A", "history-tail", CheckStatus.Fail)]
    public async Task CompleteFileNameAndUnavailableStudyIdRemainDistinct(string name, string id, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(name, id, scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal((scenario is "history" or "history-tail" ? "0000" : "0001") + "/m4/study/" + name, finding.Location.LogicalPath);
            Assert.Equal(finding.Location.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("context")]
    [InlineData("duplicate")]
    [InlineData("reverse-id")]
    public async Task ContradictoryFrozenIdentityDoesNotProduceANameFinding(string variant)
    {
        var fixture = await Fixture("stf-A.xml", "A");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var elements = stf.Elements.ToList();
        if (variant == "reverse-id")
        {
            var id = Assert.Single(elements, element => element.LocalName == "study-id");
            elements.Add(new(id.NodePath + "-extra", id.ParentPath, id.Name, id.LocalName, id.NamespaceUri, [], [], "B", id.Location));
        }
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks,
            variant == "context" ? new string('f', 64) : xml.RulesDigest, new(true, variant == "duplicate" ? [document, document] : [document]));
        fixture = ReplaceXml(fixture, altered);
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalIdentityAndIndependentCurrentScopeRemainBound(bool historical)
    {
        var fixture = await Fixture("stf-A.xml", "A", historical ? "history" : "valid");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich);
        var changedIndex = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id == "owner" ? leaf with { Operation = "delete" } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(backbone => backbone.Kind != BackboneKind.Ich).Append(changedIndex), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        Assert.Equal(historical ? CheckStatus.NotEvaluated : CheckStatus.Pass, Inspect(fixture)!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-21", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-21", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateRemainFrozen(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("wrong.xml", "A", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task FindingBudgetAndProgressStayImmutable(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("wrong.xml", "A", "two");
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
    public async Task CancellationRetainsPendingOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("wrong.xml", "A");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: result =>
        { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("stf-A.xml", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("wrong.xml", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task DeliveryAndReportDoNotOverclaimUnqualifiedCharacterPolicy(string name, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(name, "A");
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowNewRule()
    {
        var fixture = await Fixture("wrong.xml", "A");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfFileNameRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);

    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static FixtureData ReplaceXml(FixtureData fixture, PackageXmlInspection changed) => fixture with
    {
        Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != changed.InputDigest).Append(changed),
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest)
    };

    private static async Task<FixtureData> Fixture(string name, string id, string scenario = "valid", string variant = "valid", string? date = "2026-11-21")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", name, id, scenario);
        var history = scenario is "history" or "history-only" or "history-fragment" or "history-tail" ? new Capture(app, "0000", name, id, scenario == "history-tail" ? "tail" : "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var targetXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == target.InputDigest);
        var targetIndex = Assert.Single(targetXml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (scenario.StartsWith("history", StringComparison.Ordinal))
        {
            Assert.Empty(targetXml.StudyTaggingFiles.Documents);
            if (scenario == "history-only") Assert.DoesNotContain(targetIndex.Elements, element => element.Attribute("ID") == "owner");
            else Assert.Equal("../0000/m4/study/" + name + (scenario == "history-fragment" ? "#bad" : ""),
                Assert.Single(targetIndex.Elements, element => element.Attribute("ID") == "owner").Attribute("href", "http://www.w3c.org/1999/xlink"));
            if (history is not null)
            {
                var historicalXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == history.InputDigest);
                Assert.Equal("0000/m4/study/" + name, Assert.Single(historicalXml.StudyTaggingFiles.Documents).LogicalPath);
            }
        }
        else if (scenario == "ordinary") Assert.Empty(targetXml.StudyTaggingFiles.Documents);
        else
        {
            Assert.Equal(scenario == "two" ? 2 : 1, targetXml.StudyTaggingFiles.Documents.Count);
            var document = Assert.Single(targetXml.StudyTaggingFiles.Documents, item => item.LogicalPath == "0001/m4/study/" + name);
            if (scenario == "missing-id") Assert.DoesNotContain(document.Elements, element => element.LocalName == "study-id");
            if (scenario is "valid" or "unreferenced" or "two" or "char-reference")
            {
                Assert.True(document.ReadComplete);
                Assert.Equal(id, Assert.Single(document.Elements, element => element.LocalName == "study-id").Text);
            }
        }
        foreach (var xml in lifecycle.XmlInspections)
        {
            Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
            foreach (var document in xml.StudyTaggingFiles.Documents)
            {
                if (scenario is "tail" or "history-tail")
                { Assert.False(document.ReadComplete); Assert.Equal("study", Assert.Single(document.Elements, element => element.ParentPath is null).LocalName); }
                else if (scenario is "wrong-root" or "missing-root")
                    Assert.DoesNotContain(document.Elements, element => element.ParentPath is null && element.LocalName == "study");
                else if (scenario != "missing-id") Assert.Equal(CheckStatus.Pass, document.DtdStatus);
            }
        }
        if (scenario is "valid" or "unreferenced" or "two" or "char-reference")
            Assert.NotEmpty(Assert.Single(lifecycle.XmlInspections).StudyTaggingFiles.Documents);
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string name, string id, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var section = new XElement("m4-3-literature-references");
            if (scenario is not ("unreferenced" or "history-only")) section.Add(Leaf("owner", (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name + (scenario == "history-fragment" ? "#bad" : "")));
            section.Add(Leaf("report", "m4/study/report.pdf"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var studyId = scenario == "missing-id" ? "" : scenario == "char-reference" ? "<study-id>&#65;</study-id>" : new XElement("study-id", id).ToString(SaveOptions.DisableFormatting);
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>A</title>" + studyId + "</study-identifier><study-document/></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "missing-root") stf = declaration;
            if (scenario == "ordinary") stf = "%PDF-1.4 ordinary";
            if (!scenario.StartsWith("history", StringComparison.Ordinal)) _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "two") _files[sequence + "/m4/study/also-wrong.xml"] = Encoding.UTF8.GetBytes(stf);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1884-" + Manifest.SequenceNumber;
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
