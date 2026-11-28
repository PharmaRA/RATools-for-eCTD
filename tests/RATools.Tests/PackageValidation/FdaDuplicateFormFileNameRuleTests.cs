using System.Globalization;
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

public sealed class FdaDuplicateFormFileNameRuleTests
{
    private const string RuleId = "FDA47-5032";

    [Theory]
    [InlineData("duplicate", CheckStatus.Fail)]
    [InlineData("same-file", CheckStatus.NotEvaluated)]
    [InlineData("attachment", CheckStatus.NotEvaluated)]
    [InlineData("directory-marker", CheckStatus.NotEvaluated)]
    [InlineData("different-marker", CheckStatus.NotEvaluated)]
    [InlineData("missing-file", CheckStatus.NotEvaluated)]
    [InlineData("delete", CheckStatus.NotEvaluated)]
    [InlineData("different-app", CheckStatus.NotEvaluated)]
    [InlineData("same-app", CheckStatus.Fail)]
    [InlineData("same-number-other-type", CheckStatus.NotEvaluated)]
    [InlineData("shared-multiple-app", CheckStatus.NotEvaluated)]
    [InlineData("shared-single-app", CheckStatus.Fail)]
    [InlineData("different-form-type", CheckStatus.Fail)]
    [InlineData("historical", CheckStatus.NotEvaluated)]
    [InlineData("fragment-same-file", CheckStatus.NotEvaluated)]
    [InlineData("percent-same-file", CheckStatus.NotEvaluated)]
    public async Task CountsDistinctCurrentFilesForExactApplication(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        Assert.True(fixture.Files.Forms.InventoryComplete);
        Assert.All(fixture.Files.Forms.Groups, group => Assert.Equal(CheckStatus.Pass, group.DtdStatus));
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Contains("123456", finding.Actual);
            Assert.Equal(2, finding.Location.Files!.Count);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var fixture = await Fixture("duplicate", date);
        Assert.Equal(expected, Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("bad-number")]
    [InlineData("missing-type")]
    [InlineData("broken-tail")]
    public async Task UnknownPrerequisitesDoNotAuthorizeDuplicateFinding(string scenario)
    {
        var fixture = await Fixture(scenario);
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowance(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("duplicate");
        var check = Direct(fixture, remaining: remaining)!;
        Assert.Equal(expected, check.Status); Assert.Equal(remaining, check.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("duplicate"); using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token,
            progress: result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("rules")]
    [InlineData("assets")]
    [InlineData("missing-xml")]
    [InlineData("duplicate-xml")]
    [InlineData("duplicate-regional")]
    [InlineData("missing-dtd")]
    [InlineData("incomplete")]
    [InlineData("bad-children")]
    [InlineData("bad-namespace")]
    [InlineData("leaf-href")]
    [InlineData("duplicate-leaf")]
    public async Task ConflictingFrozenFactsRemainUnknown(string scenario)
    {
        var fixture = await Fixture("duplicate");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var elements = regional.Elements.Select(element => scenario is "bad-children" or "bad-namespace" && element.ParentPath is null
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
                scenario == "bad-namespace" ? "urn:wrong" : element.NamespaceUri, element.Attributes.Values,
                scenario == "bad-children" ? [] : element.Children, element.Text, element.Location) : element).ToArray();
        var leaves = scenario == "duplicate-leaf" ? regional.Leaves.Concat(regional.Leaves.Take(1)).ToArray() : regional.Leaves
            .Select(leaf => scenario == "leaf-href" && leaf.Id == "two" ? leaf with { Href = "inconsistent.pdf" } : leaf).ToArray();
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind,
            scenario != "incomplete", regional.DtdStatus, regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier,
            elements, regional.Nodes, leaves, regional.IdIndex.Values.SelectMany(value => value), scenario == "missing-dtd" ? [] : regional.ResolvedAssets);
        var documents = xml.Backbones.Select(item => item == regional ? changed : item).ToList();
        if (scenario == "duplicate-regional") documents.Add(changed);
        var modified = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId,
            scenario == "assets" ? new string('0', 64) : xml.AssetManifestDigest, xml.LimitsDigest, documents, xml.Checks,
            scenario == "rules" ? new string('0', 64) : xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Input.Packages, fixture.Lifecycle.ProfileSnapshotId,
            scenario == "missing-xml" ? [] : scenario == "duplicate-xml" ? [modified, modified] : [modified], [], [], [],
            fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var check = Direct(fixture, lifecycle)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task PartialReportDoesNotClaimFullFormCoverage()
    {
        var fixture = await Fixture("same-file");
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("href", "valid", CheckStatus.Fail)]
    [InlineData("href", "missing", CheckStatus.NotEvaluated)]
    [InlineData("href", "default", CheckStatus.NotEvaluated)]
    [InlineData("href", "conflict", CheckStatus.NotEvaluated)]
    [InlineData("operation", "missing", CheckStatus.NotEvaluated)]
    [InlineData("operation", "default", CheckStatus.NotEvaluated)]
    [InlineData("operation", "conflict", CheckStatus.NotEvaluated)]
    [InlineData("operation", "duplicate", CheckStatus.NotEvaluated)]
    [InlineData("href", "duplicate", CheckStatus.NotEvaluated)]
    [InlineData("operation", "normalized", CheckStatus.NotEvaluated)]
    [InlineData("href", "normalized", CheckStatus.NotEvaluated)]
    public async Task CountedLeafRequiresOriginalExplicitAttribute(string name, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("duplicate");
        var lifecycle = ChangeLeafAttribute(fixture, "two", name, scenario);
        var check = Direct(fixture, lifecycle)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData("one", "href")]
    [InlineData("three", "href")]
    [InlineData("one", "operation")]
    [InlineData("three", "operation")]
    public async Task UnqualifiedLeafDoesNotHideOtherKnownDuplicateFiles(string unknownId, string attribute)
    {
        var fixture = await Fixture("three-files");
        var lifecycle = ChangeLeafAttribute(fixture, unknownId, attribute, "missing");
        var check = Direct(fixture, lifecycle)!;
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        var unknownPath = "0001/m1/us/" + (unknownId == "one" ? "1571.pdf" : "extra1571.pdf");
        Assert.Equal(2, finding.Location.Files!.Count);
        Assert.DoesNotContain(finding.Location.Files, file => file.LogicalPath == unknownPath);
        Assert.Equal(CheckStatus.NotEvaluated, Direct(fixture, lifecycle, remaining: 0)!.Status);
        Assert.Single(Direct(fixture, lifecycle, remaining: 1)!.Findings);
    }

    private static PackageLifecycleInspection ChangeLeafAttribute(FixtureData fixture, string leafId, string name, string scenario)
    {
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        var leaf = Assert.Single(regional.Leaves, item => item.Id == leafId);
        var selected = Assert.Single(regional.Elements, item => item.NodePath == leaf.NodePath);
        var original = Assert.Single(selected.Attributes.Values, item => item.LocalName == name);
        Assert.False(original.IsDefault);
        Assert.Equal(original.Value, original.ExplicitValue);
        var elements = regional.Elements.Select(element => element != selected ? element : new ParsedXmlElement(
            element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
            element.Attributes.Values.Select(attribute => attribute != original ? attribute : scenario switch
            {
                "missing" => attribute with { ExplicitValue = null },
                "default" => attribute with { IsDefault = true },
                "conflict" => attribute with { ExplicitValue = "conflicting-original" },
                "normalized" => attribute with { ExplicitValue = " " + attribute.Value + " " },
                _ => attribute
            }).Concat(scenario == "duplicate" ? [original with { Name = "duplicate:" + original.LocalName }] : []), element.Children, element.Text, element.Location)).ToArray();
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind,
            regional.ReadComplete, regional.DtdStatus, regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier,
            elements, regional.Nodes, regional.Leaves, regional.IdIndex.Values.SelectMany(value => value), regional.ResolvedAssets);
        var modified = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Select(item => item == regional ? changed : item), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        return new(fixture.Input.Packages, fixture.Lifecycle.ProfileSnapshotId, [modified], [], [], [], fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("duplicate"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    [Fact]
    public async Task KnownDuplicateSurvivesAnotherUnknownApplication()
    {
        var fixture = await Fixture("later-unknown");
        Assert.True(fixture.Files.Forms.InventoryComplete);
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Single(check.Findings);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task SharedFindingBudgetAndUnreachedRuleRemainBounded(int limit)
    {
        var fixture = await Fixture("duplicate", limits: new PackageReadLimits { MaxXmlFindings = limit });
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.NotEqual(CheckStatus.Pass, check.Status);
        Assert.True(fixture.Files.Checks.Sum(item => item.Findings.Count) <= limit);
    }

    [Fact]
    public async Task FailureRemainsInfoInPartialReport()
    {
        var fixture = await Fixture("duplicate");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        var report = delivery.CreateReport(PackageValidationMode.Development);
        Assert.Equal(CheckStatus.Fail, Assert.Single(report.Coverage, item => item.RuleId == RuleId).CheckStatus);
        Assert.Equal(ValidationSeverity.Info, Assert.Single(delivery.Files.Checks, item => item.RuleId == RuleId).Findings.Single().Severity);
    }

    private static RuleCheckResult? Direct(FixtureData fixture, PackageLifecycleInspection? lifecycle = null, int remaining = 10,
        Action<RuleCheckResult>? progress = null, CancellationToken token = default) =>
        FdaDuplicateFormFileNameRule.Inspect(fixture.Input.Packages, lifecycle ?? fixture.Lifecycle, fixture.Context, remaining, token, progress);

    private sealed record FixtureData(PackageDeliveryInput Input, PackageLifecycleInspection Lifecycle, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string? date = "2026-11-25", PackageReadLimits? limits = null)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario == "missing-source")
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(scenario, limits ?? new());
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, lifecycle, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string scenario, PackageReadLimits limits)
        {
            Limits = limits;
            var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            var application = xml.Descendants("application").Single();
            var second = scenario switch
            {
                "same-file" => "1571.pdf", "attachment" => "attachment.pdf", "directory-marker" => "1571/attachment.pdf",
                "different-marker" => "356h.pdf", "historical" => "../../../0000/m1/us/other1571.pdf",
                "fragment-same-file" => "1571.pdf#page=2", "percent-same-file" => "%31%35%37%31.pdf", _ => "other1571.pdf"
            };
            var firstForm = Form("one", "1571.pdf"); var secondForm = Form("two", second);
            if (scenario == "delete") secondForm.Element("leaf")!.SetAttributeValue("operation", "delete");
            if (scenario == "different-form-type") secondForm.SetAttributeValue("form-type", "fdaft2");
            if (scenario is "shared-single-app" or "shared-multiple-app")
                xml.Descendants("m1-1-forms").Single().Add(firstForm, secondForm);
            else application.Descendants("submission-information").Single().Add(firstForm);
            if (scenario is "different-app" or "same-app" or "same-number-other-type" or "shared-multiple-app")
            {
                var other = new XElement(application);
                foreach (var form in other.Descendants("form").ToArray()) form.Remove();
                other.SetAttributeValue("application-containing-files", "false");
                var number = other.Descendants("application-number").Single();
                if (scenario is "different-app" or "shared-multiple-app") number.Value = "654321";
                if (scenario == "same-number-other-type") number.SetAttributeValue("application-type", "fdaat1");
                if (scenario != "shared-multiple-app") other.Descendants("submission-information").Single().Add(secondForm);
                application.AddAfterSelf(other);
            }
            else if (scenario == "different-form-type") xml.Descendants("m1-1-forms").Single().Add(secondForm);
            else if (scenario != "shared-single-app") firstForm.Add(secondForm.Elements("leaf"));
            if (scenario == "three-files") firstForm.Add(Form("three", "extra1571.pdf").Elements("leaf"));
            if (scenario == "later-unknown")
            {
                var other = new XElement(application);
                other.SetAttributeValue("application-containing-files", "false");
                other.Descendants("application-number").Single().Value = "unknown";
                foreach (var form in other.Descendants("form").ToArray()) form.Remove();
                other.Descendants("submission-information").Single().Add(Form("unknown", "attachment.pdf"));
                application.AddAfterSelf(other);
            }
            if (scenario == "bad-number") application.Descendants("application-number").Single().Value = " 123456 ";
            if (scenario == "missing-type") application.Descendants("application-number").Single().Attribute("application-type")!.Remove();
            var text = xml.ToString(SaveOptions.DisableFormatting);
            if (scenario == "broken-tail") text += "<broken";
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", text, PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            foreach (var path in new[] { "1571.pdf", "other1571.pdf", "attachment.pdf", "356h.pdf", "1571/attachment.pdf" })
                if (scenario != "missing-file" || path != "other1571.pdf") _files["0001/m1/us/" + path] = "fixture"u8.ToArray();
            if (scenario == "three-files") _files["0001/m1/us/extra1571.pdf"] = "fixture"u8.ToArray();
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Form(string id, string href) => new("form", new XAttribute("form-type", "fdaft1"),
            new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", "00000000000000000000000000000000"),
                new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id)));
        public string SourceId => "fda5032";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
