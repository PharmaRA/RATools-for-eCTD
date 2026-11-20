using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfDtdVersionRuleTests
{
    [Theory]
    [InlineData("explicit", CheckStatus.Pass)]
    [InlineData("omitted", CheckStatus.Fail)]
    [InlineData("foreign", CheckStatus.Fail)]
    [InlineData("char-reference", CheckStatus.Pass)]
    [InlineData("empty", CheckStatus.NotEvaluated)]
    [InlineData("space", CheckStatus.NotEvaluated)]
    [InlineData("wrong", CheckStatus.NotEvaluated)]
    [InlineData("no-root", CheckStatus.NotEvaluated)]
    [InlineData("partial-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("omitted-tail", CheckStatus.Fail)]
    [InlineData("explicit-tail", CheckStatus.NotEvaluated)]
    [InlineData("omitted-index-tail", CheckStatus.Fail)]
    [InlineData("explicit-index-tail", CheckStatus.NotEvaluated)]
    [InlineData("unreferenced", CheckStatus.Fail)]
    [InlineData("no-stf", CheckStatus.NotApplicable)]
    [InlineData("missing-stf", CheckStatus.NotEvaluated)]
    [InlineData("unknown-second", CheckStatus.Fail)]
    [InlineData("present-unknown-second", CheckStatus.NotEvaluated)]
    [InlineData("historical-only", CheckStatus.NotApplicable)]
    [InlineData("history-stf", CheckStatus.Fail)]
    [InlineData("history-reference", CheckStatus.Fail)]
    [InlineData("explicit-history-reference", CheckStatus.Pass)]
    [InlineData("history-fragment", CheckStatus.NotEvaluated)]
    public async Task RealXmlSeparatesExplicitDefaultAndUnavailableRoot(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Inspect(fixture);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("@dtd-version", finding.Location.FieldPath);
            Assert.Null(finding.Location.LeafId);
            var sequence = scenario == "history-stf" ? "0000" : "0001";
            Assert.Equal(sequence + "/m4/study/stf.xml", finding.Location.LogicalPath);
            var file = Assert.Single(finding.Location.Files!);
            Assert.Equal(fixture.Packages.Sequences[sequence].SourceId, file.SourceId);
            Assert.Equal(finding.Location.LogicalPath, file.LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("no-attribute", CheckStatus.Fail)]
    [InlineData("no-raw", CheckStatus.Pass)]
    [InlineData("hash", CheckStatus.NotEvaluated)]
    [InlineData("context", CheckStatus.NotEvaluated)]
    [InlineData("duplicate", CheckStatus.NotEvaluated)]
    [InlineData("conflicting-duplicate", CheckStatus.NotEvaluated)]
    public async Task FrozenFactsRemainBoundAndDoNotRequireAnAuxiliaryRawValue(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("explicit");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var original = Assert.Single(xml.StudyTaggingFiles.Documents);
        var elements = original.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
            element.LocalName, element.NamespaceUri, element.Attributes.Values.Where(attribute => scenario != "no-attribute" || attribute.Name != "dtd-version")
                .Select(attribute => attribute.Name == "dtd-version" ? scenario switch
                {
                    "no-raw" => attribute with { ExplicitValue = null },
                    "conflicting-duplicate" => attribute with { IsDefault = true, ExplicitValue = null },
                    _ => attribute
                } : attribute),
            element.Children, element.Text, element.Location));
        var document = new ParsedStudyTaggingFile(original.LogicalPath, scenario == "hash" ? new string('f', 64) : original.CapturedSha256,
            original.ReadComplete, original.DtdStatus, original.SystemIdentifier, elements, original.ProcessingInstructions, original.ResolvedAssets);
        var altered = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, scenario == "context" ? new string('f', 64) : xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, scenario == "duplicate" ? [document, document] :
                scenario == "conflicting-duplicate" ? [document, original] : [document]));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [altered],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        Assert.Equal(expected, FdaStfDtdVersionRule.Inspect(fixture.Packages, lifecycle, fixture.Context, 20, default)!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourcesAndEffectiveDateRemainFrozen(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("omitted", variant, date)).Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task BudgetAndSnapshotsAreSharedAndImmutable(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-stfs");
        var snapshots = new List<RuleCheckResult>();
        var check = FdaStfDtdVersionRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default,
            result => { if (result.Status == CheckStatus.Fail) snapshots.Add(result); });
        Assert.Equal(expected, check!.Status);
        Assert.Equal(budget, check.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrEstablishedOmission(bool afterFailure)
    {
        var fixture = await Fixture("omitted");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaStfDtdVersionRule.Inspect(fixture.Packages, fixture.Lifecycle,
            fixture.Context, 20, cancellation.Token, result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("explicit", CheckStatus.Pass)]
    [InlineData("omitted", CheckStatus.Fail)]
    public async Task DeliveryAndReportExpose1752IndependentlyFrom1748(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context)
            .InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1752").Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1752").CheckStatus);
        Assert.Equal(scenario == "explicit" ? CheckStatus.Fail : CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1748").Status);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("omitted");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values,
            context.Catalog.Profiles.Values, context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1752"));
        Assert.Null(FdaStfDtdVersionRule.Inspect(fixture.Packages, fixture.Lifecycle, new(catalog, context.Assets, context.NodeSchema), 20, default));
    }

    private static RuleCheckResult Inspect(FixtureData fixture) => FdaStfDtdVersionRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string variant = "valid", string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var history = scenario is "historical-only" or "history-stf" or "history-fragment" ? new Capture(app, "0000", "omitted") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (!scenario.EndsWith("index-tail", StringComparison.Ordinal)) Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        if (scenario.EndsWith("index-tail", StringComparison.Ordinal))
        {
            Assert.False(index.ReadComplete);
            Assert.Single(Assert.Single(xml.StudyTaggingFiles.Documents).Elements, element => element.ParentPath is null);
        }
        if (scenario is "empty" or "space" or "wrong" or "partial-root" or "no-root" or "wrong-root")
            Assert.Empty(Assert.Single(xml.StudyTaggingFiles.Documents).Elements);
        if (scenario is "explicit" or "omitted" or "foreign" or "char-reference" or "omitted-tail" or "explicit-tail")
        {
            var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
            var root = Assert.Single(stf.Elements, element => element.ParentPath is null);
            Assert.Equal("2.2", root.Attributes["dtd-version"].Value);
            Assert.Equal(scenario is "omitted" or "foreign" or "omitted-tail", root.Attributes["dtd-version"].IsDefault);
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
            var section = new XElement("m4-3-literature-references");
            if (scenario is not ("no-stf" or "historical-only" or "unreferenced"))
            {
                var owner = Leaf("owner", scenario == "history-stf" ? "../0000/m4/study/stf.xml" :
                    scenario == "history-fragment" ? "../0000/m4/study/stf.xml#unexpected" : "m4/study/stf.xml");
                if (scenario != "explicit") owner.SetAttributeValue("version", "present");
                section.Add(owner);
            }
            if (scenario is "unknown-second" or "present-unknown-second") section.Add(Leaf("unknown", "m4/study/stf-missing.xml"));
            section.Add(Leaf("report", "m4/study/report.pdf"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")),
                new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario.EndsWith("index-tail", StringComparison.Ordinal)) index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var attribute = scenario switch
            {
                "explicit" or "explicit-tail" or "explicit-index-tail" or "present-unknown-second" or "explicit-history-reference" => " dtd-version='2.2'",
                "char-reference" => " dtd-version='2.&#50;'",
                "foreign" => " xmlns:f='urn:foreign' f:dtd-version='2.2'",
                "empty" => " dtd-version=''", "space" => " dtd-version=' 2.2 '", "wrong" => " dtd-version='2.3'", _ => ""
            };
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'" + attribute + ">" +
                "<study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document>" +
                (scenario is "history-reference" or "explicit-history-reference" ? "<doc-content xlink:href='../../../0000/index.xml#old'/>" : "") + "</study-document></ectd:study>";
            if (scenario is "omitted-tail" or "explicit-tail") stf = stf[..^8];
            if (scenario == "no-root") stf = declaration;
            if (scenario == "partial-root") stf = declaration + "<ectd:study dtd-version='";
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario is not ("no-stf" or "historical-only" or "history-stf" or "history-fragment" or "missing-stf")) _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "two-stfs") _files[sequence + "/m4/study/stf-second.xml"] = Encoding.UTF8.GetBytes(stf);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1752-" + Manifest.SequenceNumber;
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
