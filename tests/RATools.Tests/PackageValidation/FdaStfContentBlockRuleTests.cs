using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfContentBlockRuleTests
{
    private const string RuleId = "FDA47-3029";
    private const string Doc = "<doc-content xlink:href='../../index.xml#report'><file-tag name='study-report-body' info-type='ich'/></doc-content>";
    private const string Block = "<content-block><block-title>Block</block-title>";

    [Theory]
    [InlineData("none", "valid", CheckStatus.Pass, 0)]
    [InlineData("no-docs", "valid", CheckStatus.Pass, 0)]
    [InlineData("one", "valid", CheckStatus.Fail, 1)]
    [InlineData("nested", "valid", CheckStatus.Fail, 2)]
    [InlineData("siblings", "valid", CheckStatus.Fail, 2)]
    [InlineData("empty", "valid", CheckStatus.Fail, 1)]
    [InlineData("empty-invalid", "valid", CheckStatus.Fail, 1)]
    [InlineData("other-dtd-error", "valid", CheckStatus.NotEvaluated, 0)]
    [InlineData("one", "tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("one", "unreferenced", CheckStatus.Fail, 1)]
    [InlineData("one", "ordinary", CheckStatus.NotApplicable, 0)]
    [InlineData("one", "history", CheckStatus.Fail, 1)]
    [InlineData("one", "history-only", CheckStatus.NotApplicable, 0)]
    [InlineData("one", "history-nonxml", CheckStatus.Fail, 1)]
    [InlineData("one", "history-missing-nonxml", CheckStatus.NotEvaluated, 0)]
    [InlineData("one", "missing-nonxml", CheckStatus.NotEvaluated, 0)]
    [InlineData("one", "mixed-unknown", CheckStatus.Fail, 1)]
    public async Task ActualBlocksAreLocatedIndependentlyOfTheirChildren(string body, string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(body, scenario);
        var check = Direct(fixture)!;
        Assert.Equal(expected, check.Status); Assert.Equal(count, check.Findings.Count);
        Assert.Equal(count, check.Findings.Select(item => item.Location.NodePath).Distinct().Count());
        Assert.All(check.Findings, finding =>
        {
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("content-block", finding.Location.FieldPath);
            Assert.Contains("/content-block[", finding.Location.NodePath!, StringComparison.Ordinal);
            Assert.Contains("next planned submission", finding.RecommendedAction!, StringComparison.Ordinal);
            Assert.StartsWith(scenario.StartsWith("history", StringComparison.Ordinal) ? "0000/" : "0001/", finding.Location.LogicalPath!, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("2008-03-09", "valid", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", "valid", CheckStatus.Fail)]
    [InlineData(null, "valid", CheckStatus.NotEvaluated)]
    [InlineData("2026-11-25", "missing-spec", CheckStatus.NotEvaluated)]
    [InlineData("2026-11-25", "missing-criteria", CheckStatus.NotEvaluated)]
    public async Task DateAndSourcesAreRequired(string? date, string variant, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("one", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task FindingAllowanceIsPerBlock(int remaining, CheckStatus expected, int count)
    {
        var check = Direct(await Fixture("nested"), remaining)!;
        Assert.Equal(expected, check.Status); Assert.Equal(count, check.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsProgress(bool afterFailure)
    {
        var fixture = await Fixture("nested"); using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("none", CheckStatus.Pass)]
    [InlineData("one", CheckStatus.Fail)]
    public async Task FileInspectorIncludesIndependentAdvisoryRule(string body, CheckStatus expected)
    {
        var fixture = await Fixture(body);
        var result = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("hash", "valid")]
    [InlineData("duplicate", "valid")]
    [InlineData("reverse", "valid")]
    [InlineData("location", "valid")]
    [InlineData("asset", "valid")]
    [InlineData("hash", "history")]
    [InlineData("rules", "history")]
    [InlineData("duplicate-xml", "history")]
    public async Task ConflictingFrozenIdentityCannotProveBlockUse(string variant, string scenario)
    {
        var fixture = await Fixture("one", scenario);
        var capture = fixture.Packages.Sequences[scenario == "history" ? "0000" : "0001"];
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == capture.InputDigest);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var block = Assert.Single(stf.Elements, item => item.Name == "content-block");
        var elements = stf.Elements.Select(element => variant == "reverse" && element.NodePath == block.ParentPath
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children.Where(path => path != block.NodePath), element.Text, element.Location)
            : variant == "location" && element == block ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location with { LogicalPath = "0001/other.xml" }) : element);
        var changed = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('0', 64) : stf.CapturedSha256, stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, variant == "asset" ? [] : stf.ResolvedAssets);
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks, variant == "rules" ? "other" : xml.RulesDigest,
            new(xml.StudyTaggingFiles.InventoryComplete, variant == "duplicate" ? [changed, changed] : [changed], xml.StudyTaggingFiles.Findings));
        var selected = fixture.Lifecycle.XmlInspections.Where(item => item != xml).Append(replacement);
        if (variant == "duplicate-xml") selected = selected.Append(replacement);
        var result = Direct(fixture with { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, selected, fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) })!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status); Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("address")]
    [InlineData("operation")]
    [InlineData("duplicate-index")]
    [InlineData("regional-flag")]
    public async Task HistoricalBlockRequiresQualifiedCurrentOwner(string variant)
    {
        var fixture = await Fixture("one", "history");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich);
        var leaves = index.Leaves.Select(leaf => leaf.Id != "owner" ? leaf : variant == "address" ? leaf with { Address = new(Guid.NewGuid(), "0001", "index.xml", "owner") } : variant == "operation" ? leaf with { Operation = "delete" } : variant == "regional-flag" ? leaf with { IsRegionalReference = true } : leaf);
        var changed = new ParsedBackbone(index.LogicalPath, variant == "profile" ? "other" : index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes, leaves, index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        var backbones = xml.Backbones.Where(item => item != index).Append(changed);
        if (variant == "duplicate-index") backbones = backbones.Append(changed);
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, backbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var result = Direct(fixture with { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(item => item != xml).Append(replacement), fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) })!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status); Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("none", "valid", CheckStatus.Pass)]
    [InlineData("one", "valid", CheckStatus.Fail)]
    [InlineData("one", "ordinary", CheckStatus.NotApplicable)]
    [InlineData("other-dtd-error", "valid", CheckStatus.NotEvaluated)]
    public async Task ReportPreservesAdvisorySeverityAndObservedOutcome(string body, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(body, scenario);
        Assert.Equal(ImplementationStatus.Implemented, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        Assert.All(check.Findings, item => Assert.Equal(ValidationSeverity.Warning, item.Severity));
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("one"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfContentBlockRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);

    private static async Task<FixtureData> Fixture(string body, string scenario = "valid", string variant = "valid", string? date = "2026-11-25")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-criteria" or "missing-spec")
        {
            var hash = variant == "missing-criteria" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid(); var target = new Capture(app, "0001", body, scenario);
        var history = scenario is "history" or "history-only" or "history-nonxml" ? new Capture(app, "0000", body, scenario == "history-nonxml" ? "nonxml" : "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich).DtdStatus);
        var selected = history is null ? xml : Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == history.InputDigest);
        if (scenario.StartsWith("history", StringComparison.Ordinal))
        {
            Assert.Empty(xml.StudyTaggingFiles.Documents);
            var owners = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich).Leaves.Where(leaf => leaf.Id == "owner");
            if (scenario == "history-only") Assert.Empty(owners);
            else Assert.Equal("../0000/m4/study/" + (scenario.Contains("nonxml", StringComparison.Ordinal) ? "stf-A.txt" : "stf-A.xml"), Assert.Single(owners).Href);
        }
        if (scenario is "ordinary" or "missing-nonxml" or "history-missing-nonxml") Assert.Empty(selected.StudyTaggingFiles.Documents);
        else
        {
            if (scenario == "mixed-unknown")
            {
                Assert.Equal(2, selected.StudyTaggingFiles.Documents.Count);
                Assert.False(Assert.Single(selected.StudyTaggingFiles.Documents, item => item.LogicalPath.EndsWith("stf-B.xml", StringComparison.Ordinal)).ReadComplete);
            }
            var stf = Assert.Single(selected.StudyTaggingFiles.Documents, item => item.LogicalPath.EndsWith("stf-A.xml", StringComparison.Ordinal) || item.LogicalPath.EndsWith("stf-A.txt", StringComparison.Ordinal));
            Assert.Equal(scenario != "tail", stf.ReadComplete);
            Assert.Equal(body is "empty-invalid" or "other-dtd-error" || scenario == "tail" ? CheckStatus.Fail : CheckStatus.Pass, stf.DtdStatus);
            Assert.Equal(body is "none" or "no-docs" or "other-dtd-error" ? 0 : body is "nested" or "siblings" ? 2 : 1, stf.Elements.Count(item => item.Name == "content-block"));
        }
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string body, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid()); regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var name = scenario == "ordinary" ? "metadata.xml" : scenario.Contains("nonxml", StringComparison.Ordinal) ? "stf-A.txt" : "stf-A.xml";
            var section = new XElement("m4-3-literature-references", Leaf("report", "m4/study/report.pdf"));
            if (scenario is not ("unreferenced" or "history-only")) section.Add(Leaf("owner", (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            var one = Block + Doc + "</content-block>";
            var content = body switch
            {
                "none" => Doc, "no-docs" => "", "one" => one, "nested" => Block + one + "</content-block>", "siblings" => one + one,
                "empty" => Block + "</content-block>", "empty-invalid" => "<content-block/>",
                "other-dtd-error" => "<doc-content/>", _ => throw new ArgumentOutOfRangeException(nameof(body))
            };
            var stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>Study</title><study-id>A</study-id></study-identifier><study-document>" + content + "</study-document></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing-nonxml") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            if (scenario == "mixed-unknown") _files[sequence + "/m4/study/stf-B.xml"] = Encoding.UTF8.GetBytes(stf[..^8]);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public string SourceId => "fda3029-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; set; } = new();
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
