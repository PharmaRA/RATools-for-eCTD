using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfVersionMatchRuleTests
{
    [Theory]
    [InlineData("stf version 2.2", CheckStatus.Pass)]
    [InlineData("STF version 2.2", CheckStatus.Pass)]
    [InlineData("STF Version 2.2", CheckStatus.Pass)]
    [InlineData("stf version 2.1", CheckStatus.Fail)]
    [InlineData("STF Version 3.0", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    [InlineData("", CheckStatus.NotEvaluated)]
    [InlineData("   ", CheckStatus.NotEvaluated)]
    [InlineData("abc", CheckStatus.NotEvaluated)]
    [InlineData("2.2", CheckStatus.NotEvaluated)]
    [InlineData(" stf version 2.2 ", CheckStatus.NotEvaluated)]
    [InlineData("stf  version 2.2", CheckStatus.NotEvaluated)]
    [InlineData("stf version 02.2", CheckStatus.NotEvaluated)]
    [InlineData("stf version 2.20", CheckStatus.NotEvaluated)]
    [InlineData("stf version 2.&amp;2", CheckStatus.NotEvaluated)]
    public async Task ComparesVersionTokensNotDifferentAttributeFormats(string? version, CheckStatus expected)
    {
        var check = Inspect(await Fixture(version));
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) AssertFailure(Assert.Single(check.Findings));
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("default-root", "stf version 2.2", CheckStatus.Pass)]
    [InlineData("default-root", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("index-tail", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("index-tail", "stf version 2.2", CheckStatus.NotEvaluated)]
    [InlineData("stf-tail", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("stf-tail", "stf version 2.2", CheckStatus.NotEvaluated)]
    [InlineData("partial-root", "stf version 2.1", CheckStatus.NotEvaluated)]
    [InlineData("unsupported-root", "stf version 2.1", CheckStatus.NotEvaluated)]
    [InlineData("foreign-owner", "stf version 2.1", CheckStatus.NotEvaluated)]
    [InlineData("missing-stf", "stf version 2.1", CheckStatus.NotEvaluated)]
    [InlineData("unknown-second", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("unknown-second", "stf version 2.2", CheckStatus.NotEvaluated)]
    [InlineData("unreferenced", "stf version 2.1", CheckStatus.NotApplicable)]
    [InlineData("historical-only", "stf version 2.1", CheckStatus.NotApplicable)]
    [InlineData("history-stf", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("history-stf", "stf version 2.2", CheckStatus.Pass)]
    [InlineData("history-fragment", "stf version 2.1", CheckStatus.NotEvaluated)]
    [InlineData("append", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("replace", "stf version 2.1", CheckStatus.Fail)]
    [InlineData("char-reference", "stf version 2.2", CheckStatus.Pass)]
    public async Task LocalEvidenceScopeAndHistoryStayIndependent(string scenario, string version, CheckStatus expected)
    {
        var fixture = await Fixture(version, scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            AssertFailure(finding);
            var sequence = scenario == "history-stf" ? "0000" : "0001";
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == sequence + "/m4/study/stf.xml" && file.SourceId == fixture.Packages.Sequences[sequence].SourceId);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("default-owner", CheckStatus.NotEvaluated)]
    [InlineData("no-raw", CheckStatus.Fail)]
    [InlineData("no-root-attribute", CheckStatus.NotEvaluated)]
    [InlineData("no-dtd-evidence", CheckStatus.NotEvaluated)]
    [InlineData("default-no-dtd-evidence", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-stf", CheckStatus.NotEvaluated)]
    [InlineData("conflicting-stf", CheckStatus.NotEvaluated)]
    [InlineData("stf-hash", CheckStatus.NotEvaluated)]
    [InlineData("broken-parent", CheckStatus.NotEvaluated)]
    public async Task FrozenIdentityAndVersionFactsCannotBeInvented(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("stf version 2.1", scenario == "default-no-dtd-evidence" ? "default-root" : "valid");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Elements, element => element.Attribute("ID") == "owner");
        var elements = index.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values.Select(attribute => attribute.Name == "version" ?
                attribute with { IsDefault = scenario == "default-owner", ExplicitValue = null } : attribute),
            scenario == "broken-parent" && element.NodePath == owner.ParentPath ? element.Children.Where(child => child != owner.NodePath) : element.Children,
            element.Text, element.Location));
        var changed = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes, index.Leaves,
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var stfElements = stf.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values.Where(attribute => scenario != "no-root-attribute" || attribute.Name != "dtd-version")
                .Select(attribute => scenario == "conflicting-stf" && attribute.Name == "dtd-version" ? attribute with { Value = "2.3", ExplicitValue = "2.3" } : attribute),
            element.Children, element.Text, element.Location));
        var alteredStf = new ParsedStudyTaggingFile(stf.LogicalPath, scenario == "stf-hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stfElements, stf.ProcessingInstructions,
            scenario is "no-dtd-evidence" or "default-no-dtd-evidence" ? [] : stf.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changed), xml.Checks, xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, scenario is "conflicting-stf" or "duplicate-stf" ? [stf, alteredStf] : [alteredStf]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.Equal(expected, FdaStfVersionMatchRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task EveryOwnerAndFindingBudgetArePreserved(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("stf version 2.1", "two-owners");
        var snapshots = new List<RuleCheckResult>();
        var result = FdaStfVersionMatchRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); });
        Assert.Equal(expected, result!.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Fact]
    public async Task MatchingFirstOwnerCannotHideSecondMismatch()
    {
        var check = Inspect(await Fixture("stf version 2.2", "two-owners"));
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("owner-second", Assert.Single(check.Findings).Location.LeafId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesPendingOrKnownMismatch(bool afterFailure)
    {
        var fixture = await Fixture("stf version 2.1");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfVersionMatchRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-asset", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("stf version 2.1", variant: variant, date: date)).Status);

    [Theory]
    [InlineData("stf version 2.1", CheckStatus.Fail)]
    [InlineData("STF Version 2.2", CheckStatus.Pass)]
    public async Task ReportKeepsRootOmissionSeparateFromVersionMatch(string version, CheckStatus expected)
    {
        var fixture = await Fixture(version, "default-root");
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1765").Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1765").CheckStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1752").Status);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("stf version 2.1");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1765"));
        Assert.Null(FdaStfVersionMatchRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    private static void AssertFailure(ValidationFinding finding)
    {
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.Equal("owner", finding.Location.LeafId);
        Assert.Equal("@version", finding.Location.FieldPath);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
    }
    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfVersionMatchRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string? version, string scenario = "valid", string variant = "valid", string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        if (variant == "missing-asset")
        {
            var originalAssets = snapshot.Resources.Single(resource => resource.Key == "xml-manifest");
            var manifest = JsonNode.Parse(originalAssets.ReadBytes())!;
            var assets = manifest["assets"]!.AsArray();
            foreach (var item in assets.Where(item => item!["id"]!.GetValue<string>().StartsWith("stf-", StringComparison.Ordinal)).ToArray()) assets.Remove(item);
            var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
            var rulesResource = snapshot.Resources.Single(resource => resource.Key == "rules");
            var rules = JsonNode.Parse(rulesResource.ReadBytes())!;
            rules["sources"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "ratools-package-xml-assets-v1")!["sha256"] =
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var resources = snapshot.Resources.Where(resource => resource.Key is not ("xml-manifest" or "rules" or "xml-stf-dtd" or "xml-stf-style"))
                .Append(StandardsSnapshotResource.Create("xml-manifest", originalAssets.Category, originalAssets.RelativePath, originalAssets.MediaType, bytes))
                .Append(StandardsSnapshotResource.Create("rules", rulesResource.Category, rulesResource.RelativePath, rulesResource.MediaType, Encoding.UTF8.GetBytes(rules.ToJsonString())));
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", version, scenario);
        var history = scenario is "historical-only" or "history-stf" or "history-fragment" ? new Capture(app, "0000", "stf version 9.0", "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (scenario is not ("index-tail" or "foreign-owner")) Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        if (scenario == "index-tail") { Assert.False(index.ReadComplete); Assert.Empty(index.Leaves); Assert.Contains(index.Elements, element => element.Attribute("ID") == "owner"); }
        if (scenario is "partial-root" or "unsupported-root") Assert.Empty(Assert.Single(xml.StudyTaggingFiles.Documents).Elements);
        if (scenario == "default-root") Assert.True(Assert.Single(Assert.Single(xml.StudyTaggingFiles.Documents).Elements, element => element.ParentPath is null).Attributes["dtd-version"].IsDefault);
        if (scenario == "stf-tail")
        {
            var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
            Assert.False(stf.ReadComplete);
            Assert.Equal("2.2", Assert.Single(stf.Elements, element => element.ParentPath is null).Attribute("dtd-version"));
        }
        if (scenario == "char-reference") Assert.Equal("stf version 2.2", Assert.Single(index.Elements, element => element.Attribute("ID") == "owner").Attribute("version"));
        if (variant == "missing-asset") { Assert.False(context.Assets.Assets.ContainsKey("stf-dtd")); Assert.Empty(xml.StudyTaggingFiles.Documents); }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
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
            var section = new XElement("m4-3-literature-references");
            var href = scenario == "history-stf" ? "../0000/m4/study/stf.xml" : scenario == "history-fragment" ? "../0000/m4/study/stf.xml#unexpected" : "m4/study/stf.xml";
            if (scenario is not ("historical-only" or "unreferenced"))
            {
                var owner = Leaf("owner", href, scenario is "append" or "replace" ? scenario : "new");
                if (version is not null) owner.SetAttributeValue(scenario == "foreign-owner" ? XName.Get("version", "urn:foreign") : XName.Get("version"), version);
                section.Add(owner);
            }
            if (scenario == "two-owners") { var second = Leaf("owner-second", href); second.SetAttributeValue("version", "STF Version 2.1"); section.Add(second); }
            if (scenario == "unknown-second") section.Add(Leaf("unknown", "m4/study/stf-missing.xml"));
            section.Add(Leaf("report", "m4/study/report.pdf"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            if (scenario == "char-reference") index = index.Replace("version=\"stf version 2.2\"", "version=\"stf version 2.&#50;\"", StringComparison.Ordinal);
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'" +
                (scenario == "default-root" ? "" : scenario == "unsupported-root" ? " dtd-version='2.3'" : " dtd-version='2.2'") + ">" +
                "<study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document/></ectd:study>";
            if (scenario == "stf-tail") stf = stf[..^8];
            if (scenario == "partial-root") stf = declaration + "<ectd:study dtd-version='";
            if (scenario is not ("historical-only" or "history-stf" or "history-fragment" or "missing-stf")) _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
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
        public string SourceId => "fda1765-" + Manifest.SequenceNumber;
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
