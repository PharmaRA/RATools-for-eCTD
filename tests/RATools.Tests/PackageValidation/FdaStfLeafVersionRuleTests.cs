using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfLeafVersionRuleTests
{
    [Theory]
    [InlineData(null, CheckStatus.Fail)]
    [InlineData("", CheckStatus.Fail)]
    [InlineData("   ", CheckStatus.Fail)]
    [InlineData("stf version 2.2", CheckStatus.Pass)]
    [InlineData("abc", CheckStatus.Pass)]
    [InlineData(" stf version 2.2 ", CheckStatus.Pass)]
    public async Task OnlyExplicitNonblankLeafVersionSatisfiesPresence(string? version, CheckStatus expected)
    {
        var fixture = await Fixture(version);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) AssertFailure(Assert.Single(check.Findings));
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("append", CheckStatus.Fail)]
    [InlineData("replace", CheckStatus.Fail)]
    [InlineData("history-stf", CheckStatus.Fail)]
    [InlineData("historical-only", CheckStatus.NotApplicable)]
    [InlineData("unreferenced", CheckStatus.NotApplicable)]
    [InlineData("missing-stf", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotApplicable)]
    [InlineData("declared-wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("index-tail", CheckStatus.Fail)]
    [InlineData("index-before-owner", CheckStatus.NotEvaluated)]
    [InlineData("stf-tail", CheckStatus.Fail)]
    [InlineData("stf-before-root", CheckStatus.NotEvaluated)]
    [InlineData("foreign-version", CheckStatus.Fail)]
    [InlineData("missing-doc-leaf", CheckStatus.Fail)]
    public async Task OwnersAndRetainedLocalAttributesAreIndependentOfLaterBodyFailures(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(null, scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            AssertFailure(finding);
            if (scenario == "history-stf")
                Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0000/m4/study/stf.xml" && file.SourceId == fixture.Packages.Sequences["0000"].SourceId);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("index-tail")]
    [InlineData("stf-tail")]
    [InlineData("unknown-second")]
    public async Task KnownPresenceDoesNotProveUnobservedScopeComplete(string scenario)
    {
        var fixture = await Fixture("present", scenario);
        var check = Inspect(fixture);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task MultipleOwnersAreCheckedSeparatelyAndContentLeavesAreNotStfOwners()
    {
        var fixture = await Fixture("present", "two-owners");
        var check = Inspect(fixture);
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("second-owner", finding.Location.LeafId);
        Assert.Equal("@version", finding.Location.FieldPath);
    }

    [Fact]
    public async Task KnownMissingVersionSurvivesAnotherUnidentifiedStfCandidate()
    {
        var check = Inspect(await Fixture(null, "unknown-second"));
        Assert.Equal(CheckStatus.Fail, check.Status);
        AssertFailure(Assert.Single(check.Findings));
    }

    [Theory]
    [InlineData("no-raw", CheckStatus.Pass)]
    [InlineData("default", CheckStatus.NotEvaluated)]
    public async Task RetainedCdataAndContradictoryDefaultFactsStayDistinct(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("present");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var elements = index.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values.Select(attribute => attribute.Name == "version" ?
                attribute with { IsDefault = scenario == "default", ExplicitValue = null } : attribute), element.Children, element.Text, element.Location));
        var changed = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes, index.Leaves,
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changed), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.Equal(expected, FdaStfLeafVersionRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!.Status);
    }

    [Theory]
    [InlineData("broken-parent")]
    [InlineData("ambiguous-id")]
    [InlineData("stf-hash")]
    [InlineData("xml-context")]
    public async Task ContradictoryFrozenFactsCannotEstablishOmission(string scenario)
    {
        var fixture = await Fixture(null);
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Elements, element => element.Attribute("ID") == "stf");
        var elements = index.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values.Select(attribute =>
                scenario == "ambiguous-id" && element.Attribute("ID") == "document" && attribute.Name == "ID" ?
                    attribute with { Value = "stf", ExplicitValue = "stf" } : attribute),
            scenario == "broken-parent" && element.NodePath == owner.ParentPath ? element.Children.Where(child => child != owner.NodePath) : element.Children,
            element.Text, element.Location));
        var changed = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes, index.Leaves,
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var stfs = new PackageStfInspection(xml.StudyTaggingFiles.InventoryComplete, xml.StudyTaggingFiles.Documents.Select(document =>
            new ParsedStudyTaggingFile(document.LogicalPath, scenario == "stf-hash" ? new string('f', 64) : document.CapturedSha256,
                document.ReadComplete, document.DtdStatus, document.SystemIdentifier, document.Elements, document.ProcessingInstructions, document.ResolvedAssets)));
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changed), xml.Checks,
            scenario == "xml-context" ? new string('f', 64) : xml.RulesDigest, stfs);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var result = FdaStfLeafVersionRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateRemainBound(string scenario, string? date, CheckStatus expected)
    {
        Assert.Equal(expected, Inspect(await Fixture(null, scenario, date)).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task SharedBudgetAndProgressSnapshotsAreBound(int budget, CheckStatus expected)
    {
        var fixture = await Fixture(null, "two-owners");
        var snapshots = new List<RuleCheckResult>();
        var result = FdaStfLeafVersionRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); });
        Assert.Equal(expected, result!.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(null);
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfLeafVersionRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result =>
            { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData(null, CheckStatus.Fail)]
    [InlineData("present", CheckStatus.Pass)]
    public async Task ActualDeliveryReportExposesTheRule(string? version, CheckStatus expected)
    {
        var fixture = await Fixture(version);
        foreach (var input in fixture.Packages.Sequences.Values.Cast<Capture>()) input.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context)
            .InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1748").Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, row => row.RuleId == "FDA47-1748").CheckStatus);
    }

    [Fact]
    public async Task OldCatalogCannotBorrowCurrentRule()
    {
        var fixture = await Fixture(null);
        var original = fixture.Context;
        var catalog = new ValidationRuleCatalog(original.Catalog.Version, new string('a', 64), original.Catalog.Sources.Values,
            original.Catalog.Profiles.Values, original.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1748"));
        Assert.Null(FdaStfLeafVersionRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, original.Assets, original.NodeSchema), 20, default));
    }

    private static void AssertFailure(ValidationFinding finding)
    {
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.Equal("stf", finding.Location.LeafId);
        Assert.Equal("@version", finding.Location.FieldPath);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
    }
    private static RuleCheckResult Inspect(FixtureData fixture) =>
        FdaStfLeafVersionRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string? version, string scenario = "valid", string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario is "missing-source" or "missing-spec")
        {
            var hash = scenario == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" :
                "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", version, scenario);
        var history = scenario is "history-stf" or "historical-only" ? new Capture(app, "0000", null, "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (scenario is not ("index-tail" or "index-before-owner" or "foreign-version")) Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        if (scenario == "index-tail")
        {
            Assert.False(index.ReadComplete);
            Assert.Empty(index.Leaves);
            Assert.Contains(index.Elements, element => element.Attribute("ID") == "stf");
            Assert.Single(xml.StudyTaggingFiles.Documents);
        }
        if (scenario == "stf-tail") Assert.False(Assert.Single(xml.StudyTaggingFiles.Documents).ReadComplete);
        foreach (var input in packages.Sequences.Values.Cast<Capture>()) input.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string? version, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var stfHref = scenario == "history-stf" ? "../0000/m4/study/stf.xml" : "m4/study/stf.xml";
            var leaf = Leaf("stf", stfHref, scenario is "append" or "replace" ? scenario : "new");
            if (version is not null) leaf.SetAttributeValue("version", version);
            if (scenario == "foreign-version") leaf.SetAttributeValue(XName.Get("version", "urn:foreign"), "present");
            var section = new XElement("m4-3-literature-references");
            if (scenario is not ("unreferenced" or "historical-only")) section.Add(leaf);
            if (scenario == "two-owners") section.Add(Leaf("second-owner", stfHref));
            section.Add(Leaf("document", "m4/study/report.pdf"));
            if (scenario == "unknown-second") section.Add(Leaf("unknown", "m4/study/stf-other.xml"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            if (scenario == "index-before-owner") index = index[..index.IndexOf("<leaf ID=\"stf\"", StringComparison.Ordinal)] + "<leaf ID=\"stf\" version=\"";
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
                "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" +
                "<study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document>" +
                (scenario == "missing-doc-leaf" ? "<doc-content xlink:href='../../index.xml#absent'/>" : "") + "</study-document></ectd:study>";
            if (scenario == "stf-tail") stf = stf[..^8];
            if (scenario == "stf-before-root") stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study";
            if (scenario == "wrong-root") stf = "<other/>";
            if (scenario == "declared-wrong-root") stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><other/>";
            if (scenario is not ("missing-stf" or "history-stf" or "historical-only")) _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string operation = "new")
        {
            var leaf = new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)),
                new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
            if (operation != "new") leaf.SetAttributeValue("modified-file", "../0000/index.xml#old");
            return leaf;
        }
        public bool Armed { get; set; }
        public string SourceId => "fda1748-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => Armed ? throw new InvalidOperationException("Only frozen XML observations may be consumed.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Armed ? throw new InvalidOperationException("No content reread is needed.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
