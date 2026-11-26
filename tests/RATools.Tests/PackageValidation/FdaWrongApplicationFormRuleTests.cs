using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaWrongApplicationFormRuleTests
{
    private const string RuleId = "FDA47-5038";

    [Theory]
    [InlineData("fdaat1", "fdast6", "fdasst7")]
    [InlineData("fdaat1", "fdast7", "fdasst1")]
    [InlineData("fdaat1", "fdast8", "fdasst1")]
    [InlineData("fdaat4", "fdast9", "fdasst6")]
    [InlineData("fdaat1", "fdast10", "fdasst6")]
    [InlineData("fdaat1", "fdast11", "fdasst3")]
    [InlineData("fdaat2", "fdast6", "fdasst7")]
    [InlineData("fdaat3", "fdast7", "fdasst1")]
    [InlineData("fdaat6", "fdast8", "fdasst1")]
    [InlineData("fdaat2", "fdast10", "fdasst6")]
    [InlineData("fdaat3", "fdast11", "fdasst3")]
    public async Task ExplicitNonannualStartsCannotUse2252(string applicationType, string submissionType, string subtype)
    {
        var fixture = await Fixture("original2252", activity: (applicationType, submissionType, subtype));
        Assert.True(fixture.Files.Forms.InventoryComplete);
        Assert.True(fixture.Context.FdaM1Policy!.AllowsCombination(applicationType, submissionType, subtype));
        Assert.Equal(CheckStatus.Pass, Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).Backbones, item => item.Kind == BackboneKind.UsRegional).DtdStatus);
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(ValidationSeverity.Error, Assert.Single(check.Findings).Severity);
    }

    [Theory]
    [InlineData("fdaat1", "fdast5", "fdasst6")]
    [InlineData("fdaat1", "fdast5", "fdasst4")]
    [InlineData("fdaat1", "fdast6", "fdasst4")]
    [InlineData("fdaat1", "fdast7", "fdasst4")]
    [InlineData("fdaat1", "fdast8", "fdasst5")]
    [InlineData("fdaat4", "fdast9", "fdasst4")]
    [InlineData("fdaat1", "fdast10", "fdasst4")]
    [InlineData("fdaat1", "fdast11", "fdasst5")]
    [InlineData("fdaat1", "fdast9", "fdasst6")]
    [InlineData("fdaat4", "fdast10", "fdasst6")]
    [InlineData("fdaat2", "fdast7", "fdasst1")]
    [InlineData("fdaat1", "fdast6", "fdasst1")]
    public async Task AnnualFollowupAndInvalidCombinationsDoNotBecomeNonannualStarts(string application, string type, string subtype)
    {
        var fixture = await Fixture("original2252", activity: (application, type, subtype));
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2025-03-30", CheckStatus.NotEvaluated)]
    [InlineData("2025-03-31", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task ExpandedNonannualScopeHasItsOwnDateGate(string? date, CheckStatus expected)
    {
        var fixture = await Fixture("original2252", date, activity: ("fdaat1", "fdast10", "fdasst6"));
        Assert.Equal(expected, Direct(fixture)!.Status);
    }

    [Theory]
    [InlineData("parent2252")]
    [InlineData("missing-m1")]
    public async Task ExpandedNonannualScopeRequiresStartIdentityAndArchivedSpecification(string scenario)
    {
        var fixture = await Fixture(scenario, activity: ("fdaat1", "fdast10", "fdasst6"));
        Assert.Equal(CheckStatus.NotEvaluated, Direct(fixture)!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExpandedNonannualFailureHonorsAllowanceAndCancellation(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("original2252", activity: ("fdaat1", "fdast10", "fdasst6"));
        Assert.Equal(expected, Direct(fixture, remaining: remaining)!.Status);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, remaining: 1, token: cancellation.Token,
            progress: result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Single(retained!.Findings); Assert.Equal(CheckStatus.Fail, retained.Status);
    }

    [Theory]
    [InlineData("type-default")]
    [InlineData("subtype-default")]
    [InlineData("namespace")]
    [InlineData("sequence")]
    public async Task ExpandedNonannualStartRequiresExplicitConsistentFacts(string variant)
    {
        var fixture = await Fixture("original2252", activity: ("fdaat1", "fdast10", "fdasst6"));
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var elements = regional.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
            element.NamespaceUri, element.Attributes.Values.Select(attribute =>
                variant == "type-default" && attribute.LocalName == "submission-type" || variant == "subtype-default" && attribute.LocalName == "submission-sub-type"
                    ? attribute with { IsDefault = true }
                    : variant == "namespace" && attribute.LocalName == "submission-type" ? attribute with { NamespaceUri = "urn:wrong" } : attribute),
            element.Children, variant == "sequence" && element.LocalName == "sequence-number" ? "0002" : element.Text, element.Location)).ToArray();
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind, regional.ReadComplete, regional.DtdStatus,
            regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier, elements, regional.Nodes, regional.Leaves,
            regional.IdIndex.Values.SelectMany(value => value), regional.ResolvedAssets);
        var modified = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Select(item => item == regional ? changed : item), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Input.Packages, fixture.Lifecycle.ProfileSnapshotId, [modified], [], [], [],
            fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var check = Direct(fixture, lifecycle)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task NonannualFailureSurvivesUnknownApplicationAndRemainsPartial()
    {
        var fixture = await Fixture("later-unknown2252", activity: ("fdaat1", "fdast10", "fdasst6"));
        Assert.Single(Direct(fixture)!.Findings);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("nda1571", CheckStatus.Fail)]
    [InlineData("anda1571", CheckStatus.Fail)]
    [InlineData("bla1571", CheckStatus.Fail)]
    [InlineData("ind356h", CheckStatus.Fail)]
    [InlineData("ind1571", CheckStatus.NotEvaluated)]
    [InlineData("nda356h", CheckStatus.NotEvaluated)]
    [InlineData("dmf1571", CheckStatus.NotEvaluated)]
    [InlineData("original2252", CheckStatus.Fail)]
    [InlineData("annual2252", CheckStatus.NotEvaluated)]
    [InlineData("amendment2252", CheckStatus.NotEvaluated)]
    [InlineData("parent2252", CheckStatus.NotEvaluated)]
    [InlineData("unknown2252", CheckStatus.NotEvaluated)]
    [InlineData("prefix1571", CheckStatus.NotEvaluated)]
    [InlineData("upper1571", CheckStatus.NotEvaluated)]
    [InlineData("shared-one", CheckStatus.Fail)]
    [InlineData("shared-many", CheckStatus.NotEvaluated)]
    [InlineData("other-app", CheckStatus.Fail)]
    [InlineData("missing", CheckStatus.NotEvaluated)]
    [InlineData("delete", CheckStatus.NotEvaluated)]
    [InlineData("history", CheckStatus.NotEvaluated)]
    [InlineData("query", CheckStatus.NotEvaluated)]
    [InlineData("fragment", CheckStatus.Fail)]
    [InlineData("encoded", CheckStatus.Fail)]
    [InlineData("same-file", CheckStatus.Fail)]
    public async Task ExactFilenameBranchesUseActualApplicationAssociation(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        Assert.True(fixture.Files.Forms.InventoryComplete);
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Contains(scenario == "other-app" ? "654321" : "123456", finding.Actual);
            Assert.Contains("correct application form", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var fixture = await Fixture("nda1571", date);
        Assert.Equal(expected, Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("missing-policy")]
    [InlineData("missing-type")]
    [InlineData("bad-number")]
    [InlineData("broken-tail")]
    public async Task UnknownPrerequisitesDoNotAuthorizeFailure(string scenario)
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
        var fixture = await Fixture("nda1571");
        var check = Direct(fixture, remaining: remaining)!;
        Assert.Equal(expected, check.Status); Assert.Equal(remaining, check.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("nda1571"); using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
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
    public async Task ConflictingFrozenFactsCannotAuthorizeFailure(string scenario)
    {
        var fixture = await Fixture("nda1571");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var elements = regional.Elements.Select(element => scenario is "bad-children" or "bad-namespace" && element.ParentPath is null
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
                scenario == "bad-namespace" ? "urn:wrong" : element.NamespaceUri, element.Attributes.Values,
                scenario == "bad-children" ? [] : element.Children, element.Text, element.Location) : element).ToArray();
        var leaves = scenario == "duplicate-leaf" ? regional.Leaves.Concat(regional.Leaves.Take(1)).ToArray() : regional.Leaves
            .Select(leaf => scenario == "leaf-href" ? leaf with { Href = "inconsistent.pdf" } : leaf).ToArray();
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

    [Theory]
    [InlineData("nda1571", CheckStatus.Fail)]
    [InlineData("ind1571", CheckStatus.NotEvaluated)]
    public async Task PartialReportPreservesQualifiedFailureOnly(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
        if (expected == CheckStatus.Fail) Assert.Equal(ValidationSeverity.Error, Assert.Single(delivery.Files.Checks, item => item.RuleId == RuleId).Findings.Single().Severity);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task SharedBudgetAndUnreachedResultStayBounded(int limit)
    {
        var fixture = await Fixture("nda1571", limits: new() { MaxXmlFindings = limit });
        Assert.NotEqual(CheckStatus.Pass, Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(fixture.Files.Checks.Sum(item => item.Findings.Count) <= limit);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("nda1571"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    [Fact]
    public async Task KnownWrongFormSurvivesLaterUnknownApplication()
    {
        var fixture = await Fixture("later-unknown");
        Assert.True(fixture.Files.Forms.InventoryComplete);
        var check = Assert.Single(fixture.Files.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status); Assert.Single(check.Findings);
    }

    private static RuleCheckResult? Direct(FixtureData fixture, PackageLifecycleInspection? lifecycle = null, int remaining = 10,
        Action<RuleCheckResult>? progress = null, CancellationToken token = default) =>
        FdaWrongApplicationFormRule.Inspect(fixture.Input.Packages, lifecycle ?? fixture.Lifecycle, fixture.Context, remaining, token, progress);

    private sealed record FixtureData(PackageDeliveryInput Input, PackageLifecycleInspection Lifecycle, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string? date = "2026-11-25", PackageReadLimits? limits = null,
        (string Application, string Submission, string Subtype)? activity = null)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario is "missing-source" or "missing-policy")
        {
            var removed = scenario == "missing-source"
                ? snapshot.Resources.Where(item => item.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c").ToArray()
                : snapshot.Resources.Where(item => item.Key == "fda-m1-attribute-policy").ToArray();
            Assert.Single(removed);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != removed[0].Sha256).ToArray() },
                snapshot.Resources.Except(removed), snapshot.AcquiredUtc);
        }
        if (scenario == "missing-m1")
        {
            const string m1Hash = "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2";
            Assert.Single(snapshot.Resources, item => item.Sha256 == m1Hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != m1Hash).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != m1Hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(scenario, limits ?? new(), activity);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, lifecycle, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string scenario, PackageReadLimits limits, (string Application, string Submission, string Subtype)? activity = null)
        {
            Limits = limits;
            var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            var application = xml.Descendants("application").Single();
            var type = scenario switch { "anda1571" => "fdaat2", "bla1571" => "fdaat3", "ind356h" or "ind1571" or "other-app" => "fdaat4", "dmf1571" => "fdaat5", _ => "fdaat1" };
            application.Descendants("application-number").Single().SetAttributeValue("application-type", type);
            var filename = scenario.Contains("2252", StringComparison.Ordinal) || scenario == "missing-m1" ? "2252.pdf" : scenario is "ind356h" or "nda356h" ? "356h.pdf" : "1571.pdf";
            var href = scenario switch
            {
                "prefix1571" => "other1571.pdf", "upper1571" => "1571.PDF", "history" => "../../../0000/m1/us/1571.pdf",
                "query" => "1571.pdf?view=1", "fragment" => "1571.pdf#page=1", "encoded" => "%31%35%37%31.pdf", _ => filename
            };
            var form = Form("main", href);
            if (scenario == "delete") form.Element("leaf")!.SetAttributeValue("operation", "delete");
            if (scenario == "same-file") form.Add(Leaf("alias", "1571.pdf#page=2"));
            if (scenario.StartsWith("shared-", StringComparison.Ordinal)) xml.Descendants("m1-1-forms").Single().Add(form);
            else application.Descendants("submission-information").Single().Add(form);
            if (scenario is "other-app" or "shared-many")
            {
                var other = new XElement(application);
                other.SetAttributeValue("application-containing-files", "false");
                other.Descendants("application-number").Single().Value = "654321";
                other.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat1");
                foreach (var existing in other.Descendants("form").ToArray()) existing.Remove();
                if (scenario == "other-app") other.Descendants("submission-information").Single().Add(Form("other", "1571.pdf"));
                application.AddAfterSelf(other);
            }
            if (scenario is "later-unknown" or "later-unknown2252")
            {
                var other = new XElement(application);
                other.SetAttributeValue("application-containing-files", "false");
                other.Descendants("application-number").Single().Value = "unknown";
                foreach (var existing in other.Descendants("form").ToArray()) existing.Remove();
                other.Descendants("submission-information").Single().Add(Form("unknown", "1571.pdf"));
                application.AddAfterSelf(other);
            }
            var id = application.Descendants("submission-id").Single();
            var sequence = application.Descendants("sequence-number").Single();
            if (activity is { } declared)
            {
                application.Descendants("application-number").Single().SetAttributeValue("application-type", declared.Application);
                id.SetAttributeValue("submission-type", declared.Submission);
                sequence.SetAttributeValue("submission-sub-type", declared.Subtype);
            }
            if (scenario == "annual2252") { id.SetAttributeValue("submission-type", "fdast5"); sequence.SetAttributeValue("submission-sub-type", "fdasst6"); }
            if (scenario == "amendment2252") sequence.SetAttributeValue("submission-sub-type", "fdasst4");
            if (scenario == "parent2252") id.Value = "0000";
            if (scenario == "unknown2252") id.SetAttributeValue("submission-type", "unknown");
            if (scenario == "bad-number") application.Descendants("application-number").Single().Value = " 123456 ";
            if (scenario == "missing-type") application.Descendants("application-number").Single().Attribute("application-type")!.Remove();
            var text = xml.ToString(SaveOptions.DisableFormatting);
            if (scenario == "broken-tail") text += "<broken";
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", text, PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (scenario != "missing")
                foreach (var path in new[] { "1571.pdf", "356h.pdf", "2252.pdf", "other1571.pdf", "1571.PDF" })
                    _files["0001/m1/us/" + path] = FdaPdfFontEmbeddingRuleTests.Build("Symbol");
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Form(string id, string href) => new("form", new XAttribute("form-type", "fdaft2"), Leaf(id, href));
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
            new XAttribute("checksum", "00000000000000000000000000000000"), new XAttribute("checksum-type", "md5"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public string SourceId => "fda5038";
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
