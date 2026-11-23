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

public sealed class FdaStfStudyTitlePresenceRuleTests
{
    private const string RuleId = "FDA47-1985";

    [Theory]
    [InlineData("present", CheckStatus.Pass)]
    [InlineData("missing", CheckStatus.Fail)]
    [InlineData("no-identifier", CheckStatus.Fail)]
    [InlineData("doc-title-only", CheckStatus.Fail)]
    [InlineData("empty", CheckStatus.NotEvaluated)]
    [InlineData("space", CheckStatus.NotEvaluated)]
    [InlineData("duplicate", CheckStatus.NotEvaluated)]
    [InlineData("tail-missing", CheckStatus.NotEvaluated)]
    [InlineData("tail-present", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("missing-file", CheckStatus.NotEvaluated)]
    [InlineData("ordinary", CheckStatus.NotApplicable)]
    [InlineData("unreferenced", CheckStatus.Fail)]
    [InlineData("history", CheckStatus.Fail)]
    [InlineData("history-only", CheckStatus.NotApplicable)]
    [InlineData("history-missing", CheckStatus.NotEvaluated)]
    [InlineData("history-missing-nonxml", CheckStatus.NotEvaluated)]
    public async Task CompleteResearchTitleAbsenceDoesNotBorrowDocumentTitle(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal((scenario == "history" ? "0000" : "0001") + "/m4/study/stf-A.xml", finding.Location.LogicalPath);
            Assert.Equal("study-identifier/title", finding.Location.FieldPath);
            Assert.Single(finding.Location.Files!);
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("context")]
    [InlineData("missing-hash")]
    [InlineData("missing-context")]
    [InlineData("duplicate-observation")]
    [InlineData("unlisted-title")]
    [InlineData("missing-title-element")]
    [InlineData("cycle-title")]
    public async Task ContradictoryFrozenPresenceCannotBecomeOmissionOrPass(string variant)
    {
        var fixture = await Fixture(variant.StartsWith("missing-", StringComparison.Ordinal) && variant != "missing-title-element" ? "missing" : "present");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
        var title = stf.Elements.SingleOrDefault(element => element.ParentPath == identifier.NodePath && element.LocalName == "title");
        var titlePath = title?.NodePath;
        if (variant is "missing-title-element" or "unlisted-title" or "cycle-title") Assert.NotNull(titlePath);
        var elements = stf.Elements.Where(element => variant != "missing-title-element" || element.NodePath != titlePath)
            .Select(element => variant is "unlisted-title" or "cycle-title" && element.NodePath == identifier.NodePath ?
                new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                    element.Attributes.Values, element.Children.Where(path => path != titlePath), element.Text, element.Location) :
                variant == "cycle-title" && element.NodePath == titlePath ? new ParsedXmlElement(element.NodePath, element.NodePath,
                    element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, [element.NodePath], element.Text, element.Location) : element);
        var altered = new ParsedStudyTaggingFile(stf.LogicalPath, variant is "hash" or "missing-hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones,
            xml.Checks, variant is "context" or "missing-context" ? new string('f', 64) : xml.RulesDigest, new(xml.StudyTaggingFiles.InventoryComplete, variant == "duplicate-observation" ? [altered, altered] : [altered])));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("address-application")]
    [InlineData("address-backbone")]
    [InlineData("address-id")]
    [InlineData("profile")]
    [InlineData("derived-delete")]
    public async Task HistoricalSelectionRequiresCurrentOwnerQualification(string variant)
    {
        var fixture = await Fixture("history");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (variant == "profile") Assert.NotEqual(PackageValidationCatalog.IchProfile, index.ProfileSnapshotId);
        var altered = new ParsedBackbone(index.LogicalPath, variant == "profile" ? PackageValidationCatalog.IchProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id != "owner" ? leaf : variant == "address" ? leaf with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0000", "index.xml", "owner") } :
                variant == "address-application" ? leaf with { Address = new(Guid.NewGuid(), "0001", "index.xml", "owner") } :
                variant == "address-backbone" ? leaf with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0001", "other.xml", "owner") } :
                variant == "address-id" ? leaf with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0001", "index.xml", "other") } :
                variant == "derived-delete" ? leaf with { Operation = "delete" } : leaf), index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(altered), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("valid", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", "2013-04-30", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("missing", variant, date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task FindingsShareBudgetAndImmutableSnapshots(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two");
        var snapshots = new List<RuleCheckResult>();
        var result = Inspect(fixture, budget, check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); })!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2)
        {
            Assert.Equal(2, snapshots[1].Findings.Count);
            Assert.Collection(result.Findings.Select(finding => finding.Location.LogicalPath).Order(StringComparer.Ordinal),
                path => Assert.Equal("0001/m4/study/stf-A.xml", path), path => Assert.Equal("0001/m4/study/stf-B.xml", path));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrKnownOmission(bool afterFailure)
    {
        var fixture = await Fixture("missing");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: check =>
        { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Fact]
    public async Task PresentTitleDoesNotSpendFindingBudget()
        => Assert.Equal(CheckStatus.Pass, Inspect(await Fixture("present"), 0)!.Status);

    [Fact]
    public async Task AlreadyCancelledTokenPropagates()
    {
        var fixture = await Fixture("present");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token));
    }

    [Theory]
    [InlineData("present", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("missing", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("ordinary", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    [InlineData("empty", CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    public async Task PartialReportDoesNotOverclaimEmptyTitleSemantics(string scenario, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("missing");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfStudyTitlePresenceRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
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
            Assert.DoesNotContain(snapshot.Resources, resource => resource.Sha256 == hash);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var history = scenario is "history" or "history-only" ? new Capture(app, "0000", "missing") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
        var selected = history is null ? xml : Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == history.InputDigest);
        if (scenario is "ordinary" or "missing-file" or "history-missing" or "history-missing-nonxml") Assert.Empty(selected.StudyTaggingFiles.Documents);
        else
        {
            Assert.Equal(scenario == "two" ? 2 : 1, selected.StudyTaggingFiles.Documents.Count);
            foreach (var stf in selected.StudyTaggingFiles.Documents)
            {
            if (scenario.StartsWith("tail", StringComparison.Ordinal))
            {
                Assert.False(stf.ReadComplete);
                var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
                Assert.Equal(scenario == "tail-present" ? 1 : 0, stf.Elements.Count(element => element.ParentPath == identifier.NodePath && element.LocalName == "title"));
            }
            else if (scenario == "wrong-root") Assert.Empty(stf.Elements);
            else
            {
                Assert.True(stf.ReadComplete);
                var root = Assert.Single(stf.Elements, element => element.ParentPath is null);
                Assert.Equal("study", root.LocalName);
                if (scenario is "present" or "empty" or "space") Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
                else Assert.Equal(CheckStatus.Fail, stf.DtdStatus);
                if (scenario == "no-identifier") Assert.DoesNotContain(stf.Elements, element => element.LocalName == "study-identifier");
                else
                {
                    var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
                    var titles = stf.Elements.Where(element => element.ParentPath == identifier.NodePath && element.LocalName == "title").ToArray();
                    Assert.Equal(scenario is "present" or "empty" or "space" ? 1 : scenario == "duplicate" ? 2 : 0, titles.Length);
                    if (titles.Length == 1) Assert.Equal(scenario == "present" ? "Study A" : scenario == "empty" ? "" : " ", titles[0].Text);
                }
                if (scenario == "doc-title-only")
                {
                    var title = Assert.Single(stf.Elements, element => element.LocalName == "title");
                    Assert.Equal("doc-content", stf.Elements.Single(element => element.NodePath == title.ParentPath).LocalName);
                }
            }
            }
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        if (scenario == "history-missing-nonxml")
        {
            Assert.Empty(packages.Baseline.Entries);
            Assert.Equal("../0000/m4/study/stf-A.txt", Assert.Single(Assert.Single(xml.Backbones, backbone => backbone.Kind == BackboneKind.Ich).Leaves, leaf => leaf.Id == "owner").Href);
            Assert.True(PackageStfBindingReader.Read(packages, lifecycle, context, default).TargetScopeComplete);
        }
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
            var section = new XElement("m4-3-literature-references");
            if (scenario is not ("unreferenced" or "history-only")) section.Add(Leaf("owner", (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            var title = scenario is "present" or "tail-present" ? "<title>Study A</title>" : scenario == "empty" ? "<title/>" : scenario == "space" ? "<title> </title>" : scenario == "duplicate" ? "<title>A</title><title>B</title>" : "";
            var identifier = scenario == "no-identifier" ? "" : "<study-identifier>" + title + "<study-id>A</study-id></study-identifier>";
            var docs = scenario == "doc-title-only" ? "<doc-content xlink:href='../../index.xml#owner'><title>Document only</title></doc-content>" : "";
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" + identifier + "<study-document>" + docs + "</study-document></ectd:study>";
            if (scenario.StartsWith("tail", StringComparison.Ordinal)) stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing-file") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "two") _files[sequence + "/m4/study/stf-B.xml"] = Encoding.UTF8.GetBytes(stf);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1985-" + Manifest.SequenceNumber;
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
