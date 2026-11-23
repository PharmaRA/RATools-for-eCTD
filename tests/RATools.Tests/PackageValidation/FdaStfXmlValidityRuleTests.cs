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

public sealed class FdaStfXmlValidityRuleTests
{
    private const string RuleId = "FDA47-1969";

    [Theory]
    [InlineData("valid", CheckStatus.Pass)]
    [InlineData("missing-id", CheckStatus.Fail)]
    [InlineData("empty-root", CheckStatus.Fail)]
    [InlineData("wrong-order", CheckStatus.Fail)]
    [InlineData("tail", CheckStatus.Fail)]
    [InlineData("declaration-only", CheckStatus.NotEvaluated)]
    [InlineData("empty-file", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("remote-dtd", CheckStatus.NotEvaluated)]
    [InlineData("missing", CheckStatus.NotEvaluated)]
    [InlineData("empty-id", CheckStatus.NotEvaluated)]
    [InlineData("space-id", CheckStatus.NotEvaluated)]
    [InlineData("unreferenced", CheckStatus.Fail)]
    [InlineData("ordinary", CheckStatus.NotApplicable)]
    [InlineData("history", CheckStatus.Fail)]
    [InlineData("history-only", CheckStatus.NotApplicable)]
    [InlineData("history-missing", CheckStatus.NotEvaluated)]
    [InlineData("history-missing-nonxml", CheckStatus.NotEvaluated)]
    public async Task KnownStructuralInvalidityIsNotConfusedWithUnavailableIdentity(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = Inspect(fixture)!;
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal((scenario == "history" ? "0000" : "0001") + "/m4/study/stf-A.xml", finding.Location.LogicalPath);
            Assert.Single(finding.Location.Files!);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("XML_CHARACTER_LIMIT", CheckStatus.Fail, false, CheckStatus.NotEvaluated)]
    [InlineData("XML_STRUCTURE_LIMIT", CheckStatus.Fail, false, CheckStatus.NotEvaluated)]
    [InlineData("UNTRUSTED_XML_RESOURCE", CheckStatus.Fail, false, CheckStatus.NotEvaluated)]
    [InlineData("BACKBONE_READ_FAILED", CheckStatus.Fail, false, CheckStatus.NotEvaluated)]
    [InlineData("STF_DISCOVERY_UNREADABLE", CheckStatus.NotEvaluated, false, CheckStatus.NotEvaluated)]
    [InlineData("DTD_VALIDATION_ERROR", CheckStatus.NotEvaluated, false, CheckStatus.NotEvaluated)]
    [InlineData("DTD_VALIDATION_ERROR", CheckStatus.Fail, true, CheckStatus.NotEvaluated)]
    [InlineData("XML_NOT_WELL_FORMED", CheckStatus.Fail, true, CheckStatus.NotEvaluated)]
    [InlineData("XML_NOT_WELL_FORMED", CheckStatus.Fail, false, CheckStatus.Fail)]
    [InlineData("DTD_VALIDATION_ERROR", CheckStatus.Fail, false, CheckStatus.Fail)]
    public async Task FrozenDiagnosticCategorySeverityAndPathMustAllMatch(string code, CheckStatus status, bool otherPath, CheckStatus expected)
    {
        var fixture = await Fixture("valid");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, stf.CapturedSha256, false, CheckStatus.Fail,
            stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        var finding = new ValidationFinding(code is "XML_CHARACTER_LIMIT" or "XML_STRUCTURE_LIMIT" ? "XML-LIMITS" :
            code == "UNTRUSTED_XML_RESOURCE" ? "XML-OFFLINE" : "STF-DTD", status, ValidationSeverity.Error, code,
            "Synthetic category evidence; message text is not a classifier.", new("0001", otherPath ? "0001/m4/study/other.xml" : stf.LogicalPath));
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, xml.RulesDigest, new(false, [document], [finding])));
        var check = Inspect(fixture)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("context")]
    [InlineData("duplicate")]
    [InlineData("no-root")]
    [InlineData("unlisted-id")]
    public async Task ContradictoryIdentityCannotBorrowAnInvalidityDiagnostic(string variant)
    {
        var fixture = await Fixture(variant == "unlisted-id" ? "missing-id" : "tail");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var elements = variant == "no-root" ? [] : stf.Elements.ToList();
        if (variant == "unlisted-id")
        {
            var identifier = Assert.Single(elements, element => element.LocalName == "study-identifier");
            var path = identifier.NodePath + "/study-id[1]";
            elements.Add(new(path, identifier.NodePath, "study-id", "study-id", "", [], [], "A", identifier.Location with { NodePath = path }));
        }
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones,
            xml.Checks, variant == "context" ? new string('f', 64) : xml.RulesDigest,
            new(false, variant == "duplicate" ? [document, document] : [document], variant == "unlisted-id" ? [] : xml.StudyTaggingFiles.Findings)));
        var check = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("empty-root")]
    public async Task CompleteStructureCanProveAbsenceWithoutDiagnosticText(string scenario)
    {
        var fixture = await Fixture(scenario);
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, xml.RulesDigest, new(false, xml.StudyTaggingFiles.Documents, [])));
        Assert.Equal(CheckStatus.Fail, Inspect(fixture)!.Status);
    }

    [Fact]
    public async Task DetachedSelfCycleCannotProveMissingStudyId()
    {
        var fixture = await Fixture("valid");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
        var id = Assert.Single(stf.Elements, element => element.LocalName == "study-id");
        var elements = stf.Elements.Select(element => element.NodePath == identifier.NodePath ?
            new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values, element.Children.Where(path => path != id.NodePath), element.Text, element.Location) :
            element.NodePath == id.NodePath ? new ParsedXmlElement(element.NodePath, element.NodePath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values, [element.NodePath], element.Text, element.Location) : element).ToArray();
        Assert.Single(elements, element => element.ParentPath is null);
        Assert.True(stf.ReadComplete);
        Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
        var altered = new ParsedStudyTaggingFile(stf.LogicalPath, stf.CapturedSha256, stf.ReadComplete, stf.DtdStatus,
            stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, xml.RulesDigest, new(true, [altered], [])));
        var check = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task AlreadyCancelledTokenPropagates()
    {
        var fixture = await Fixture("valid");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token));
    }

    [Fact]
    public async Task EndToEndWorkLimitDoesNotBecomePass()
    {
        var fixture = await Fixture("valid");
        var capture = Assert.IsType<Capture>(fixture.Packages.Target);
        capture.Limits = new() { MaxXmlNodes = 1 };
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, capture.Limits.Digest(),
            xml.Backbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        Assert.Equal(CheckStatus.NotEvaluated, Inspect(fixture)!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task PerDocumentFailuresKeepSharedBudgetAndImmutableProgress(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two");
        var progress = new List<RuleCheckResult>();
        var check = Inspect(fixture, budget, result => { if (result.Status == CheckStatus.Fail) progress.Add(result); })!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(budget, check.Findings.Count);
        if (budget > 0) Assert.Single(progress[0].Findings);
        if (budget == 2) Assert.Equal(2, progress[1].Findings.Count);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("missing-id", variant, date))!.Status);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrKnownInvalidity(bool afterFailure)
    {
        var fixture = await Fixture("missing-id");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: result =>
        { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("missing-id", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("ordinary", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    public async Task PartialReportRetainsIncompleteLoadQualification(string scenario, CheckStatus raw, CheckStatus coverage)
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
        var fixture = await Fixture("missing-id");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfXmlValidityRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
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
        var history = scenario is "history" or "history-only" ? new Capture(app, "0000", "missing-id") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var targetXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == target.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(targetXml.Backbones, backbone => backbone.Kind == BackboneKind.Ich).DtdStatus);
        var selected = history is null ? targetXml : Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == history.InputDigest);
        if (scenario is "empty-file" or "missing" or "ordinary" or "history-missing" or "history-missing-nonxml") Assert.Empty(selected.StudyTaggingFiles.Documents);
        else
        {
            Assert.Equal(scenario == "two" ? 2 : 1, selected.StudyTaggingFiles.Documents.Count);
            var stf = selected.StudyTaggingFiles.Documents[0];
            if (scenario is "missing-id" or "unreferenced" or "history" or "history-only" or "empty-root" or "wrong-order" or "two")
            {
                Assert.True(stf.ReadComplete);
                Assert.Equal(CheckStatus.Fail, stf.DtdStatus);
                Assert.Contains(selected.StudyTaggingFiles.Findings, finding => finding.Code == "DTD_VALIDATION_ERROR" && finding.CheckStatus == CheckStatus.Fail && finding.Location.LogicalPath == stf.LogicalPath);
            }
            else if (scenario == "tail")
            {
                Assert.False(stf.ReadComplete);
                Assert.Single(stf.Elements, element => element.ParentPath is null && element.LocalName == "study");
                Assert.Contains(selected.StudyTaggingFiles.Findings, finding => finding.Code == "XML_NOT_WELL_FORMED" && finding.CheckStatus == CheckStatus.Fail && finding.Location.LogicalPath == stf.LogicalPath);
            }
            else if (scenario is "declaration-only" or "wrong-root" or "remote-dtd") Assert.Empty(stf.Elements);
            else Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
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
            var section = new XElement("m4-3-literature-references");
            if (scenario is not ("unreferenced" or "history-only")) section.Add(Leaf("owner", (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var id = scenario is "missing-id" or "unreferenced" or "two" ? "" : scenario == "empty-id" ? "<study-id/>" : scenario == "space-id" ? "<study-id> </study-id>" : "<study-id>A</study-id>";
            var body = "<study-identifier><title>A</title>" + id + "</study-identifier><study-document/>";
            if (scenario == "empty-root") body = "";
            if (scenario == "wrong-order") body = "<study-document/><study-identifier><title>A</title><study-id>A</study-id></study-identifier>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'>" + body + "</ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "declaration-only") stf = declaration;
            if (scenario == "empty-file") stf = "";
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "remote-dtd") stf = stf.Replace("../../util/dtd/ich-stf-v2-2.dtd", "https://not-contacted.invalid/stf.dtd", StringComparison.Ordinal);
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "two") _files[sequence + "/m4/study/stf-B.xml"] = Encoding.UTF8.GetBytes(stf);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1969-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; set; } = new();
        public Stream OpenRead(string path) => Armed ? throw new InvalidOperationException("Use frozen XML only.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Armed ? throw new InvalidOperationException("Use frozen identity only.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
