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

public sealed class FdaEmptyLowestHeadingRuleTests
{
    private const string RuleId = "FDA47-3078";
    [Theory]
    [InlineData("empty", CheckStatus.Fail, 1)]
    [InlineData("leaf", CheckStatus.Pass, 0)]
    [InlineData("delete-leaf", CheckStatus.Pass, 0)]
    [InlineData("two-empty", CheckStatus.Fail, 2)]
    [InlineData("parent-only", CheckStatus.NotEvaluated, 0)]
    [InlineData("extension", CheckStatus.NotEvaluated, 0)]
    [InlineData("mixed", CheckStatus.Fail, 1)]
    [InlineData("tail", CheckStatus.NotEvaluated, 0)]
    public async Task LowestStandardHeadingIsNotAnyEmptyXmlElement(string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(scenario);
        var index = Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).Backbones, item => item.Kind == BackboneKind.Ich);
        Assert.Equal(scenario != "tail", index.ReadComplete);
        if (scenario != "tail") Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        var result = Direct(fixture)!;
        Assert.Equal(expected, result.Status); Assert.Equal(count, result.Findings.Count);
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("leaf", finding.Location.FieldPath);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Contains("No corrective action is necessary", finding.RecommendedAction!, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("2013-04-29", "valid", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", "valid", CheckStatus.Fail)]
    [InlineData(null, "valid", CheckStatus.NotEvaluated)]
    [InlineData("2026-11-25", "missing-criteria", CheckStatus.NotEvaluated)]
    public async Task DateAndSourceAreExplicit(string? date, string variant, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("empty", date, variant))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    [InlineData(2, CheckStatus.Fail, 2)]
    public async Task FindingAllowanceIsPerHeading(int remaining, CheckStatus expected, int count)
    {
        var result = Direct(await Fixture("two-empty"), remaining)!;
        Assert.Equal(expected, result.Status); Assert.Equal(count, result.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsProgress(bool afterFailure)
    {
        var fixture = await Fixture("two-empty"); using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("empty", CheckStatus.Fail)]
    [InlineData("leaf", CheckStatus.Pass)]
    public async Task FileInspectionIncludesIndependentHeadingCheck(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var files = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        Assert.Equal(expected, Assert.Single(files.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("duplicate-xml")]
    [InlineData("assets")]
    [InlineData("limits")]
    [InlineData("rules")]
    [InlineData("internal-profile")]
    [InlineData("reverse-child")]
    [InlineData("location")]
    public async Task ConflictingFrozenFactsCannotProveLeafAbsence(string variant)
    {
        var fixture = await Fixture("empty");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.Ich);
        var heading = Assert.Single(index.Elements, item => item.Name == "m2-2-introduction");
        var elements = index.Elements.Select(element => variant == "reverse-child" && element.NodePath == heading.ParentPath
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children.Where(path => path != heading.NodePath), element.Text, element.Location)
            : variant == "location" && element == heading ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location with { LogicalPath = "0001/other.xml" }) : element);
        var changed = new ParsedBackbone(index.LogicalPath, variant == "internal-profile" ? "other" : index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes, index.Leaves, index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, variant == "assets" ? "other" : xml.AssetManifestDigest, variant == "limits" ? "other" : xml.LimitsDigest, xml.Backbones.Where(item => item != index).Append(changed), xml.Checks, variant == "rules" ? "other" : xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, variant == "duplicate-xml" ? [replacement, replacement] : [replacement], fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var check = Direct(fixture with { Lifecycle = lifecycle })!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("empty", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("leaf", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("extension", CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    public async Task PartialReportDoesNotClaimRegionalOrExtensionCoverage(string scenario, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("empty"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaEmptyLowestHeadingRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);
    private static async Task<FixtureData> Fixture(string scenario, string? date = "2026-11-25", string variant = "valid")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant == "missing-criteria")
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var target = new Capture(scenario);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string scenario)
        {
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var heading = new XElement("m2-2-introduction");
            if (scenario == "leaf") heading.Add(Leaf("report", "report.pdf"));
            if (scenario == "delete-leaf") heading.Add(new XElement("leaf", new XAttribute("ID", "old"), new XAttribute("operation", "delete"), new XAttribute("modified-file", "../0000/index.xml#old"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XElement("title", "old")));
            if (scenario == "extension") heading.Add(new XElement("node-extension", new XElement("title", "Extension"), Leaf("report", "report.pdf")));
            var module2 = new XElement("m2-common-technical-document-summaries");
            if (scenario != "parent-only") module2.Add(heading);
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), module2);
            if (scenario is "two-empty" or "mixed") root.Add(new XElement("m4-nonclinical-study-reports", new XElement("m4-3-literature-references", scenario == "mixed" ? new XElement("node-extension", new XElement("title", "Extension"), Leaf("report", "report.pdf")) : null)));
            var text = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "tail") text = text[..^8];
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes(text);
            _files["0001/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public string SourceId => "fda3078";
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
