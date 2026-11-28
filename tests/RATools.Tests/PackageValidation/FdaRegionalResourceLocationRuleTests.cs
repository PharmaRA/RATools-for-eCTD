using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRegionalResourceLocationRuleTests
{
    private const string RuleId = "FDA47-2003";
    private const string OfficialDtd = "https://www.accessdata.fda.gov/static/eCTD/us-regional-v3-3.dtd";
    private const string OfficialStyle = "https://www.accessdata.fda.gov/static/eCTD/us-regional.xsl";

    [Theory]
    [InlineData("tail", "../../util/style/us-regional.xsl")]
    [InlineData("file-style-tail", "file:///resources/us-regional.xsl")]
    public async Task ObservedStylesheetViolationSurvivesLaterXmlTailDamage(string scenario, string expectedHref)
    {
        var fixture = await Fixture(scenario);
        var regional = Assert.Single(fixture.Xml.Backbones, b => b.Kind == BackboneKind.UsRegional);
        Assert.False(regional.ReadComplete);
        Assert.Empty(regional.ProcessingInstructions);
        Assert.Single(regional.PartialProcessingInstructions);
        Assert.Single(regional.Elements, e => e.ParentPath is null && e.LocalName == "fda-regional");
        Assert.Contains(fixture.Xml.Checks.SelectMany(c => c.Findings), f => f.Code == "XML_NOT_WELL_FORMED" && f.Location.LogicalPath == regional.LogicalPath);
        var result = Direct(fixture)!;
        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(expectedHref, Assert.Single(result.Findings, f => f.Location.FieldPath == "xml-stylesheet/@href").Actual);
    }

    [Theory]
    [InlineData("no-pi-tail")]
    [InlineData("late-pi-tail")]
    public async Task UnobservedPiAfterDamageDoesNotBecomeAStylesheetFinding(string scenario)
    {
        var fixture = await Fixture(scenario);
        var regional = Assert.Single(fixture.Xml.Backbones, b => b.Kind == BackboneKind.UsRegional);
        Assert.False(regional.ReadComplete);
        Assert.Empty(regional.ProcessingInstructions);
        Assert.Empty(regional.PartialProcessingInstructions);
        var result = Direct(fixture)!;
        Assert.Equal("doctype/system", Assert.Single(result.Findings).Location.FieldPath);
    }

    [Theory]
    [InlineData("official")]
    [InlineData("http")]
    public async Task PartialPiWithoutActualRegionalRootCannotEstablishViolation(string scenario)
    {
        var fixture = await Fixture(scenario);
        var regional = Assert.Single(fixture.Xml.Backbones, b => b.Kind == BackboneKind.UsRegional);
        Assert.Empty(regional.Elements);
        Assert.Single(regional.PartialProcessingInstructions);
        var result = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("wrong-path", CheckStatus.NotEvaluated)]
    [InlineData("wrong-sequence", CheckStatus.NotEvaluated)]
    [InlineData("wrong-target", CheckStatus.NotEvaluated)]
    [InlineData("malformed", CheckStatus.NotEvaluated)]
    [InlineData("official", CheckStatus.NotEvaluated)]
    [InlineData("missing-href", CheckStatus.NotEvaluated)]
    public async Task PartialPiRequiresBoundActualInstruction(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("tail");
        fixture = WithRegional(fixture, document => Copy(document, system: OfficialDtd, partialInstructions:
        [new(scenario == "wrong-target" ? "other" : "xml-stylesheet",
            scenario == "malformed" ? "href='unterminated" : scenario == "missing-href" ? "type='text/xsl'" :
                "type='text/xsl' href='" + (scenario == "official" ? OfficialStyle : "../../util/style/us-regional.xsl") + "'",
            new(scenario == "wrong-sequence" ? "0000" : "0001", scenario == "wrong-path" ? "0001/other.xml" : document.LogicalPath))]));
        var result = Direct(fixture)!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, result.Findings.Count);
        Assert.Equal(CheckStatus.NotEvaluated, Direct(fixture, 0)!.Status);
    }

    [Theory]
    [InlineData("local", CheckStatus.Fail)]
    [InlineData("official", CheckStatus.NotEvaluated)]
    [InlineData("http", CheckStatus.NotEvaluated)]
    [InlineData("tail", CheckStatus.Fail)]
    public async Task ActualDeclarationsAreSeparatedFromResolverAcceptance(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var regional = Assert.Single(fixture.Xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        if (scenario == "local") { Assert.True(regional.ReadComplete); Assert.Equal(CheckStatus.Pass, regional.DtdStatus); Assert.Equal("../../util/dtd/us-regional-v3-3.dtd", regional.SystemIdentifier); Assert.Contains(regional.ProcessingInstructions, pi => pi.Target == "xml-stylesheet" && pi.Data.Contains("../../util/style/us-regional.xsl", StringComparison.Ordinal)); }
        if (scenario is "official" or "http")
        {
            Assert.False(regional.ReadComplete);
            Assert.Contains(fixture.Xml.Checks.SelectMany(check => check.Findings), finding => finding.Code == "UNTRUSTED_XML_RESOURCE" && finding.Location.LogicalPath == regional.LogicalPath);
        }
        if (scenario == "tail") { Assert.False(regional.ReadComplete); Assert.Empty(regional.ProcessingInstructions); Assert.NotEmpty(regional.Elements); Assert.Equal("../../util/dtd/us-regional-v3-3.dtd", regional.SystemIdentifier); }
        var files = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        var check = Assert.Single(files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) { Assert.NotEmpty(check.Findings); Assert.All(check.Findings, finding => Assert.Equal(ValidationSeverity.Error, finding.Severity)); }
        else Assert.Empty(check.Findings);
        if (scenario == "tail")
        {
            Assert.Equal(2, check.Findings.Count);
            Assert.Contains(check.Findings, f => f.Location.FieldPath == "doctype/system");
            Assert.Contains(check.Findings, f => f.Location.FieldPath == "xml-stylesheet/@href");
        }
    }

    [Theory]
    [InlineData("local-dtd", CheckStatus.Fail, "doctype/system")]
    [InlineData("local-style", CheckStatus.Fail, "xml-stylesheet/@href")]
    [InlineData("official", CheckStatus.NotEvaluated, null)]
    [InlineData("unknown-uri", CheckStatus.NotEvaluated, null)]
    [InlineData("http", CheckStatus.NotEvaluated, null)]
    [InlineData("fragment", CheckStatus.NotEvaluated, null)]
    [InlineData("network-path", CheckStatus.NotEvaluated, null)]
    [InlineData("file-uri", CheckStatus.Fail, "doctype/system")]
    [InlineData("style-network-path", CheckStatus.NotEvaluated, null)]
    [InlineData("missing-pi", CheckStatus.NotEvaluated, null)]
    [InlineData("incomplete-style", CheckStatus.NotEvaluated, null)]
    public async Task FrozenLocationEvidenceTestsEachBranchWithoutClaimingOfficialUrlParsing(string scenario, CheckStatus expected, string? field)
    {
        var fixture = await Fixture("local");
        var dtd = scenario == "local-dtd" ? "../../util/dtd/us-regional-v3-3.dtd" : scenario == "unknown-uri" ? "https://unknown.invalid/file.dtd" : scenario == "http" ? OfficialDtd.Replace("https:", "http:", StringComparison.Ordinal) : scenario == "fragment" ? OfficialDtd + "#fragment" : scenario == "network-path" ? "//unknown.invalid/file.dtd" : scenario == "file-uri" ? "file:///outside.dtd" : OfficialDtd;
        var style = scenario is "local-style" or "incomplete-style" ? "../../util/style/us-regional.xsl" : scenario == "style-network-path" ? "//unknown.invalid/style.xsl" : OfficialStyle;
        fixture = WithRegional(fixture, document => Copy(document, system: dtd, complete: scenario != "incomplete-style", instructions: scenario == "missing-pi" ? [] : [new("xml-stylesheet", "type='text/xsl' href='" + style + "'", new("0001", document.LogicalPath, Line: 2, Column: 1))]));
        var check = Direct(fixture)!;
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal(field, Assert.Single(check.Findings).Location.FieldPath);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(false, "file:///C:/resources/us-regional-v3-3.dtd")]
    [InlineData(false, "FILE:///resources/us-regional-v3-3.dtd")]
    [InlineData(true, "file:///resources/us-regional.xsl")]
    [InlineData(true, "file:///resources/us%20regional.xsl")]
    public async Task ExplicitFileUrisAreLocalResources(bool stylesheet, string value)
    {
        var fixture = await Fixture("local");
        fixture = WithRegional(fixture, document => Copy(document, system: stylesheet ? OfficialDtd : value,
            instructions: [new("xml-stylesheet", "type='text/xsl' href='" + (stylesheet ? value : OfficialStyle) + "'",
                new("0001", document.LogicalPath, Line: 2, Column: 1))]));
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal(stylesheet ? "xml-stylesheet/@href" : "doctype/system", finding.Location.FieldPath);
        Assert.Equal(value, finding.Actual);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public async Task ActualFileStylesheetPiIsReportedWithoutOpeningTheResource()
    {
        var fixture = await Fixture("file-style");
        var regional = Assert.Single(fixture.Xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Contains(regional.ProcessingInstructions, item => item.Data.Contains("file:///resources/us-regional.xsl", StringComparison.Ordinal));
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(2, check.Findings.Count);
        Assert.Equal("file:///resources/us-regional.xsl", Assert.Single(check.Findings, item => item.Location.FieldPath == "xml-stylesheet/@href").Actual);
    }

    [Theory]
    [InlineData("file://server/share/style.xsl")]
    [InlineData("file:////server/share/style.xsl")]
    [InlineData("file:///resources/style.xsl?version=1")]
    [InlineData("file:///resources/style.xsl#part")]
    [InlineData("file:///resources/%ZZ.xsl")]
    [InlineData("file:relative.xsl")]
    [InlineData(" file:///resources/style.xsl")]
    [InlineData("file:///resources/style.xsl ")]
    [InlineData("file:///resources/us regional.xsl")]
    [InlineData("file:///C:\\resources\\style.xsl")]
    [InlineData("file:///resources/\u0009style.xsl")]
    [InlineData("https://example.invalid/style.xsl")]
    public async Task UnqualifiedUriFormsDoNotBecomeFileUriFailures(string value)
    {
        var fixture = await Fixture("local");
        fixture = WithRegional(fixture, document => Copy(document, system: value,
            instructions: [new("xml-stylesheet", "type='text/xsl' href='" + OfficialStyle + "'", new("0001", document.LogicalPath))]));
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(false, 0, CheckStatus.NotEvaluated)]
    [InlineData(false, 1, CheckStatus.Fail)]
    [InlineData(true, 1, CheckStatus.NotEvaluated)]
    public async Task FileStyleRequiresCompletePiEvidenceAndFindingCapacity(bool incomplete, int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("local");
        fixture = WithRegional(fixture, document => Copy(document, system: OfficialDtd, complete: !incomplete,
            instructions: [new("xml-stylesheet", "type='text/xsl' href='file:///resources/style.xsl'", new("0001", document.LogicalPath))]));
        var check = Direct(fixture, remaining)!;
        Assert.Equal(expected, check.Status);
        Assert.True(check.Findings.Count <= remaining);
    }

    [Theory]
    [InlineData("2015-06-14", CheckStatus.NotApplicable)]
    [InlineData("2015-06-15", CheckStatus.NotEvaluated)]
    [InlineData("2025-03-30", CheckStatus.NotEvaluated)]
    [InlineData("2025-03-31", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task CurrentSpecificationIsNotBackdated(string? date, CheckStatus expected)
    {
        var fixture = await Fixture("local", date: date);
        Assert.Equal(expected, Direct(fixture)!.Status);
    }

    [Theory]
    [InlineData("missing-m1")]
    [InlineData("missing-criteria")]
    [InlineData("input")]
    [InlineData("assets")]
    [InlineData("profile")]
    [InlineData("duplicate")]
    [InlineData("no-root")]
    [InlineData("root-location")]
    public async Task MissingSourcesOrConflictingIdentityDoNotProveLocationViolation(string scenario)
    {
        var fixture = await Fixture("local", variant: scenario);
        if (scenario is "input" or "assets" or "profile" or "duplicate")
        {
            var xml = fixture.Xml;
            var changed = new PackageXmlInspection(scenario == "input" ? "other" : xml.InputDigest, scenario == "profile" ? "other" : xml.ProfileSnapshotId,
                scenario == "assets" ? "other" : xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
            fixture = WithXml(fixture, changed, scenario == "duplicate");
        }
        if (scenario is "no-root" or "root-location") fixture = WithRegional(fixture, document => Copy(document, elements: scenario == "no-root" ? [] : document.Elements.Select(element => element.ParentPath is null ? new ParsedXmlElement(element.NodePath, null, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location with { LogicalPath = "0001/other.xml" }) : element)));
        Assert.Equal(CheckStatus.NotEvaluated, Direct(fixture)!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task BudgetRetainsIndividualDtdAndStyleFailures(int remaining, CheckStatus expected, int count)
    {
        var check = Direct(await Fixture("local"), remaining)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        if (count > 0) Assert.Equal("doctype/system", check.Findings[0].Location.FieldPath);
        if (count == 2) Assert.Equal("xml-stylesheet/@href", check.Findings[1].Location.FieldPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesProgress(bool afterFailure)
    {
        var fixture = await Fixture("local");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("local", CheckStatus.Fail)]
    [InlineData("tail", CheckStatus.Fail)]
    [InlineData("official", CheckStatus.NotEvaluated)]
    public async Task PartialReportDoesNotClaimApprovedUrlOrVersionQualification(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("local");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaRegionalResourceLocationRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context, PackageXmlInspection Xml);
    private static FixtureData WithXml(FixtureData fixture, PackageXmlInspection xml, bool duplicate = false) => fixture with { Xml = xml, Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, duplicate ? [xml, xml] : [xml], fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };
    private static FixtureData WithRegional(FixtureData fixture, Func<ParsedBackbone, ParsedBackbone> change)
    {
        var xml = fixture.Xml;
        return WithXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones.Select(document => document.Kind == BackboneKind.UsRegional ? change(document) : document), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
    }
    private static ParsedBackbone Copy(ParsedBackbone document, string? system = null, bool? complete = null, IEnumerable<ParsedXmlProcessingInstruction>? instructions = null, IEnumerable<ParsedXmlElement>? elements = null,
        IEnumerable<ParsedXmlProcessingInstruction>? partialInstructions = null)
        => new(document.LogicalPath, document.ProfileSnapshotId, document.Kind, complete ?? document.ReadComplete, document.DtdStatus, document.DocumentTypeName, system ?? document.SystemIdentifier, document.PublicIdentifier, elements ?? document.Elements, document.Nodes, document.Leaves, document.IdIndex.Values.SelectMany(ids => ids), document.ResolvedAssets, instructions ?? document.ProcessingInstructions, partialInstructions ?? document.PartialProcessingInstructions);

    private static async Task<FixtureData> Fixture(string scenario, string? date = "2026-11-23", string variant = "valid")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-m1" or "missing-criteria")
        {
            var hash = variant == "missing-m1" ? "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2" : "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var target = new Capture(scenario);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        return new(packages, lifecycle, context, Assert.Single(lifecycle.XmlInspections));
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string scenario)
        {
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var regional = Encoding.UTF8.GetString(_files["0001/m1/us/us-regional.xml"]);
            Assert.Contains("../../util/dtd/us-regional-v3-3.dtd", regional, StringComparison.Ordinal);
            if (scenario is "official" or "http") regional = regional.Replace("../../util/dtd/us-regional-v3-3.dtd", scenario == "http" ? OfficialDtd.Replace("https:", "http:", StringComparison.Ordinal) : OfficialDtd, StringComparison.Ordinal);
            const string stylePi = "<?xml-stylesheet type='text/xsl' href='../../util/style/us-regional.xsl'?>";
            if (scenario is not ("no-pi-tail" or "late-pi-tail")) regional = regional.Insert(regional.IndexOf("<!DOCTYPE", StringComparison.Ordinal), stylePi);
            if (scenario is "file-style" or "file-style-tail") regional = regional.Replace("../../util/style/us-regional.xsl", "file:///resources/us-regional.xsl", StringComparison.Ordinal);
            if (scenario is "tail" or "file-style-tail") regional = regional[..^8];
            if (scenario is "no-pi-tail" or "late-pi-tail") regional = regional[..^8] + (scenario == "late-pi-tail" ? stylePi : "");
            _files["0001/m1/us/us-regional.xml"] = Encoding.UTF8.GetBytes(regional);
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda2003-target";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
