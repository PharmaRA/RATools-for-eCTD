using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaSchemaValidityRuleTests
{
    private const string RuleId = "FDA47-2002";

    [Theory]
    [InlineData("valid", CheckStatus.Pass)]
    [InlineData("index-dtd", CheckStatus.Fail)]
    [InlineData("regional-dtd", CheckStatus.Fail)]
    [InlineData("stf-dtd", CheckStatus.Fail)]
    [InlineData("index-tail", CheckStatus.Fail)]
    [InlineData("regional-tail", CheckStatus.Fail)]
    [InlineData("stf-tail", CheckStatus.Fail)]
    [InlineData("index-offline", CheckStatus.NotEvaluated)]
    [InlineData("regional-io", CheckStatus.NotEvaluated)]
    [InlineData("stf-offline", CheckStatus.NotEvaluated)]
    [InlineData("mixed", CheckStatus.Fail)]
    public async Task RealXmlDiagnosticsAreNotConfusedWithUnavailableInspection(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var diagnostics = fixture.Xml.Checks.SelectMany(check => check.Findings).Concat(fixture.Xml.StudyTaggingFiles.Findings).ToArray();
        if (expected == CheckStatus.Fail)
        {
            var path = scenario.StartsWith("index", StringComparison.Ordinal) ? "0001/index.xml" : scenario.StartsWith("regional", StringComparison.Ordinal) ? "0001/m1/us/us-regional.xml" : "0001/m4/study/stf-A.xml";
            Assert.Contains(diagnostics, finding => finding.Location.LogicalPath == path && finding.CheckStatus == CheckStatus.Fail && finding.Code == (scenario.EndsWith("tail", StringComparison.Ordinal) ? "XML_NOT_WELL_FORMED" : "DTD_VALIDATION_ERROR"));
        }
        if (scenario == "valid")
        {
            Assert.All(fixture.Xml.Backbones, item => { Assert.True(item.ReadComplete); Assert.Equal(CheckStatus.Pass, item.DtdStatus); });
            Assert.Equal(CheckStatus.Pass, Assert.Single(fixture.Xml.StudyTaggingFiles.Documents).DtdStatus);
        }
        if (scenario.Contains("offline", StringComparison.Ordinal)) Assert.Contains(diagnostics, finding => finding.Code == "UNTRUSTED_XML_RESOURCE" && finding.RuleId == "XML-OFFLINE" && finding.CheckStatus == CheckStatus.Fail && finding.Location.SequenceNumber == "0001" && finding.Location.LogicalPath == (scenario == "index-offline" ? "0001/index.xml" : "0001/m4/study/stf-A.xml"));
        if (scenario is "regional-io" or "mixed") Assert.Contains(diagnostics, finding => finding.Code == "BACKBONE_READ_FAILED" && finding.CheckStatus == CheckStatus.Fail && finding.Location.SequenceNumber == "0001" && finding.Location.LogicalPath == "0001/m1/us/us-regional.xml");
        if (scenario is "stf-dtd" or "stf-tail" or "mixed") Assert.Single(Assert.Single(fixture.Xml.StudyTaggingFiles.Documents).Elements, element => element.ParentPath is null && element.Name == "ectd:study" && element.NamespaceUri == "http://www.ich.org/ectd");
        if (scenario.EndsWith("tail", StringComparison.Ordinal)) Assert.False(scenario == "stf-tail" ? Assert.Single(fixture.Xml.StudyTaggingFiles.Documents).ReadComplete : Assert.Single(fixture.Xml.Backbones, item => item.Kind == (scenario == "index-tail" ? BackboneKind.Ich : BackboneKind.UsRegional)).ReadComplete);
        var files = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        var check = Assert.Single(files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("0001", finding.Location.SequenceNumber);
            Assert.NotNull(finding.Location.LogicalPath);
            Assert.NotNull(finding.Location.Line);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("DTD_VALIDATION_ERROR", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("XML_NOT_WELL_FORMED", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("DTD_VALIDATION_ERROR", CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    [InlineData("BACKBONE_READ_FAILED", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("XML_CHARACTER_LIMIT", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("XML_STRUCTURE_LIMIT", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("XML_FINDING_LIMIT", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("UNTRUSTED_XML_RESOURCE", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    [InlineData("BACKBONE_PROFILE_MISMATCH", CheckStatus.Fail, CheckStatus.NotEvaluated)]
    public async Task FrozenDiagnosticCategoriesAreNotInferredFromSummaryStatus(string code, CheckStatus diagnosticStatus, CheckStatus expected)
    {
        var fixture = await Fixture("index-dtd");
        var original = fixture.Xml.Checks.SelectMany(check => check.Findings).First(finding => finding.Code == "DTD_VALIDATION_ERROR" && finding.Location.LogicalPath == "0001/index.xml");
        var changed = new PackageXmlInspection(fixture.Xml.InputDigest, fixture.Xml.ProfileSnapshotId, fixture.Xml.AssetManifestDigest, fixture.Xml.LimitsDigest, fixture.Xml.Backbones,
            [new(original.RuleId, CheckStatus.Fail, "Synthetic diagnostic consumer test", [original with { Code = code, CheckStatus = diagnosticStatus }])], fixture.Xml.RulesDigest, fixture.Xml.StudyTaggingFiles);
        var check = Direct(WithXml(fixture, changed));
        Assert.Equal(expected, check!.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("profile")]
    [InlineData("assets")]
    [InlineData("rules")]
    [InlineData("limits")]
    [InlineData("duplicate-xml")]
    [InlineData("duplicate-document")]
    [InlineData("wrong-rule")]
    [InlineData("wrong-path")]
    [InlineData("wrong-sequence")]
    [InlineData("no-diagnostic")]
    [InlineData("document-profile")]
    public async Task ConflictingOrUnboundFrozenFactsCannotProveViolation(string scenario)
    {
        var fixture = await Fixture("index-dtd");
        var xml = fixture.Xml;
        var diagnostics = xml.Checks.SelectMany(check => check.Findings).Where(finding => finding.Code == "DTD_VALIDATION_ERROR" && finding.Location.LogicalPath == "0001/index.xml").Select(finding => finding with
        {
            RuleId = scenario == "wrong-rule" ? "OTHER" : finding.RuleId,
            Location = finding.Location with { LogicalPath = scenario == "wrong-path" ? "0001/elsewhere.xml" : finding.Location.LogicalPath, SequenceNumber = scenario == "wrong-sequence" ? "0000" : finding.Location.SequenceNumber }
        }).ToArray();
        var index = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich);
        var backbones = scenario == "duplicate-document" ? xml.Backbones.Append(index) : scenario == "document-profile" ? xml.Backbones.Select(item => item == index ? new ParsedBackbone(item.LogicalPath, "other", item.Kind, item.ReadComplete, item.DtdStatus, item.DocumentTypeName, item.SystemIdentifier, item.PublicIdentifier, item.Elements, item.Nodes, item.Leaves, item.IdIndex.Values.SelectMany(ids => ids), item.ResolvedAssets) : item) : xml.Backbones;
        var changed = new PackageXmlInspection(scenario == "input" ? "other" : xml.InputDigest, scenario == "profile" ? "other" : xml.ProfileSnapshotId,
            scenario == "assets" ? "other" : xml.AssetManifestDigest, scenario == "limits" ? "other" : xml.LimitsDigest, backbones,
            [new(diagnostics[0].RuleId, CheckStatus.Fail, "synthetic", scenario == "no-diagnostic" ? [] : diagnostics)], scenario == "rules" ? "other" : xml.RulesDigest, xml.StudyTaggingFiles);
        var check = Direct(WithXml(fixture, changed, scenario == "duplicate-xml"));
        Assert.Equal(CheckStatus.NotEvaluated, check!.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    public async Task FindingAllowanceIsExact(int remaining, CheckStatus expected, int count)
    {
        var fixture = await Fixture("index-dtd");
        var check = Direct(fixture, remaining);
        Assert.Equal(expected, check!.Status);
        Assert.Equal(count, check.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesProgressAndPropagates(bool afterFailure)
    {
        var fixture = await Fixture("index-dtd");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.NotNull(retained);
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained.Status);
    }

    [Theory]
    [InlineData("2015-06-14", CheckStatus.NotApplicable)]
    [InlineData("2015-06-15", CheckStatus.Fail)]
    public async Task EffectiveDateIsRespected(string date, CheckStatus expected)
    {
        var fixture = await Fixture("index-dtd", DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected, Direct(fixture)!.Status);
    }

    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaSchemaValidityRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);
    private static FixtureData WithXml(FixtureData fixture, PackageXmlInspection xml, bool duplicate = false) => fixture with { Xml = xml, Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, duplicate ? [xml, xml] : [xml], fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };

    [Theory]
    [InlineData("hash")]
    [InlineData("no-root")]
    [InlineData("duplicate")]
    [InlineData("assets")]
    public async Task StfIdentityConflictsCannotBorrowLocatedErrors(string scenario)
    {
        var fixture = await Fixture("stf-dtd");
        var stf = Assert.Single(fixture.Xml.StudyTaggingFiles.Documents);
        var changed = new ParsedStudyTaggingFile(stf.LogicalPath, scenario == "hash" ? new string('0', 64) : stf.CapturedSha256, stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier,
            scenario == "no-root" ? [] : stf.Elements, stf.ProcessingInstructions, scenario == "assets" ? [] : stf.ResolvedAssets);
        var xml = fixture.Xml;
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks, xml.RulesDigest,
            new(false, scenario == "duplicate" ? [changed, changed] : [changed], xml.StudyTaggingFiles.Findings));
        Assert.Equal(CheckStatus.NotEvaluated, Direct(WithXml(fixture, replacement))!.Status);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("stf-dtd", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task PartialReportDoesNotClaimAttributeFileCoverage(string scenario, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("valid");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    [Theory]
    [InlineData("valid", CheckStatus.NotEvaluated)]
    [InlineData("regional-dtd", CheckStatus.NotEvaluated)]
    [InlineData("stf-dtd", CheckStatus.Fail)]
    [InlineData("custom-regional", CheckStatus.NotEvaluated)]
    public async Task MissingRegionalObservationDoesNotEstablishSchemaPass(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var path = "0001/m1/us/" + (scenario == "custom-regional" ? "custom.xml" : "us-regional.xml");
        Assert.Contains(fixture.Target.Manifest.Files, file => file.LogicalPath == path);
        var xml = fixture.Xml;
        Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        Assert.Contains(Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich).Leaves, leaf => leaf.IsRegionalReference && leaf.Href == path[5..]);
        var changed = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(item => item.Kind != BackboneKind.UsRegional), xml.Checks, xml.RulesDigest, new(true, xml.StudyTaggingFiles.Documents, xml.StudyTaggingFiles.Findings));
        Assert.DoesNotContain(changed.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var check = Direct(WithXml(fixture, changed))!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    private sealed record FixtureData(Capture Target, PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context, PackageXmlInspection Xml);

    private static async Task<FixtureData> Fixture(string scenario, DateOnly? date = null)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date ?? new DateOnly(2026, 11, 23));
        var target = new Capture(scenario);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections);
        return new(target, packages, lifecycle, context, xml);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly bool _regionalIo;
        public Capture(string scenario)
        {
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => scenario == "custom-regional" && file.LogicalPath == "0001/m1/us/us-regional.xml" ? "0001/m1/us/custom.xml" : file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var index = "<!DOCTYPE ectd:ectd SYSTEM 'util/dtd/ich-ectd-3-2.dtd'><ectd:ectd xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3c.org/1999/xlink' dtd-version='3.2'><m1-administrative-information-and-prescribing-information>" + Leaf("regional", "m1/us/us-regional.xml") + "</m1-administrative-information-and-prescribing-information><m4-nonclinical-study-reports><m4-3-literature-references>" + Leaf("owner", "m4/study/stf-A.xml") + "</m4-3-literature-references></m4-nonclinical-study-reports></ectd:ectd>";
            if (scenario == "index-dtd") index = index.Replace("operation='new'", "operation='unsupported'", StringComparison.Ordinal);
            if (scenario == "index-tail") index = index[..^8];
            if (scenario == "index-offline") index = index.Replace("util/dtd/ich-ectd-3-2.dtd", "https://not-contacted.invalid/ich.dtd", StringComparison.Ordinal);
            if (scenario == "custom-regional") index = index.Replace("m1/us/us-regional.xml", "m1/us/custom.xml", StringComparison.Ordinal);
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes(index);
            var regionalPath = "0001/m1/us/" + (scenario == "custom-regional" ? "custom.xml" : "us-regional.xml");
            var regional = Encoding.UTF8.GetString(_files[regionalPath]);
            if (scenario == "regional-dtd") regional = regional.Replace("</fda-regional:fda-regional>", "<unknown/></fda-regional:fda-regional>", StringComparison.Ordinal);
            if (scenario == "regional-tail") regional = regional[..^8];
            _files[regionalPath] = Encoding.UTF8.GetBytes(regional);
            _regionalIo = scenario is "regional-io" or "mixed";
            var stf = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>A</title>" + (scenario is "stf-dtd" or "mixed" ? "" : "<study-id>A</study-id>") + "</study-identifier><study-document/></ectd:study>";
            if (scenario == "stf-tail") stf = stf[..^8];
            if (scenario == "stf-offline") stf = stf.Replace("../../util/dtd/ich-stf-v2-2.dtd", "https://not-contacted.invalid/stf.dtd", StringComparison.Ordinal);
            _files["0001/m4/study/stf-A.xml"] = Encoding.UTF8.GetBytes(stf);
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Leaf(string id, string href) => "<leaf ID='" + id + "' operation='new' checksum='" + new string('0', 32) + "' checksum-type='md5' xlink:href='" + href + "'><title>" + id + "</title></leaf>";
        public string SourceId => "fda2002-target";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; set; } = new();
        public Stream OpenRead(string path) => _regionalIo && path == "0001/m1/us/us-regional.xml" ? throw new IOException("Deliberate read failure.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
