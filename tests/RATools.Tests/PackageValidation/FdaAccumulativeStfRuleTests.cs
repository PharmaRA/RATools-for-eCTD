using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaAccumulativeStfRuleTests
{
    [Theory]
    [InlineData("increment", CheckStatus.Pass)]
    [InlineData("repeated", CheckStatus.Fail)]
    [InlineData("ancestor", CheckStatus.Fail)]
    [InlineData("identifier-only", CheckStatus.Pass)]
    [InlineData("reuse-pdf", CheckStatus.Pass)]
    [InlineData("changed-tag", CheckStatus.NotEvaluated)]
    [InlineData("changed-block-property", CheckStatus.NotEvaluated)]
    [InlineData("missing-history", CheckStatus.NotEvaluated)]
    [InlineData("independent", CheckStatus.NotEvaluated)]
    [InlineData("changed-study-id", CheckStatus.Fail)]
    [InlineData("historical-violation", CheckStatus.Pass)]
    [InlineData("reverted-tag", CheckStatus.NotEvaluated)]
    [InlineData("multiple-owner", CheckStatus.NotEvaluated)]
    [InlineData("current-delete", CheckStatus.NotEvaluated)]
    [InlineData("historical-delete", CheckStatus.NotEvaluated)]
    [InlineData("first", CheckStatus.Pass)]
    [InlineData("current-append", CheckStatus.Pass)]
    [InlineData("current-replace", CheckStatus.Pass)]
    [InlineData("unrelated-history", CheckStatus.Pass)]
    [InlineData("target-bad-reference", CheckStatus.Fail)]
    [InlineData("predecessor-bad-reference", CheckStatus.Fail)]
    [InlineData("property-order", CheckStatus.Fail)]
    [InlineData("historical-stf-unavailable", CheckStatus.NotEvaluated)]
    [InlineData("multiple-repeat-owners", CheckStatus.Fail)]
    [InlineData("historical-stf-hash", CheckStatus.NotEvaluated)]
    [InlineData("new-with-modified", CheckStatus.Fail)]
    public async Task ActualMultiSequenceXmlSeparatesAccumulativeReferences(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = FdaAccumulativeStfRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
        var graph = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.True(expected == check.Status, $"Expected {expected}; actual {check.Status}; targetScope={graph.TargetScopeComplete}; " +
            string.Join("; ", graph.Documents.Select(document => document.Document.LogicalPath + " structure=" + document.StructureComplete +
                " owners=" + string.Join(',', document.Owners.Select(owner => owner.Leaf.Id + ":" + owner.Qualified + ":" + owner.Leaf.ContextComplete + ":" + owner.Leaf.ContextKey)))));
        if (expected != CheckStatus.Fail) Assert.Empty(check.Findings);
        else
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("0002/m4/study/stf.xml", finding.Location.LogicalPath);
            Assert.Equal("@xlink:href", finding.Location.FieldPath);
            Assert.Contains("doc-content", finding.Location.NodePath, StringComparison.Ordinal);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
        }
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingBudgetRemainsBound(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("repeated");
        var check = FdaAccumulativeStfRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(budget, check.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsExplicitIncompleteOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("repeated");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaAccumulativeStfRule.Inspect(fixture.Packages,
            fixture.Lifecycle, fixture.Context, 20, cancellation.Token, check =>
            {
                retained = check;
                if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel();
            }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-criteria", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceAndSelectionDateAreRequired(string scenario, string? date, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, date);
        var check = FdaAccumulativeStfRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default)!;
        Assert.Equal(expected, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task LegacyCatalogDoesNotInventTheRule()
    {
        var fixture = await Fixture("increment");
        var current = fixture.Context;
        var catalog = new ValidationRuleCatalog(current.Catalog.Version, new string('a', 64), current.Catalog.Sources.Values,
            current.Catalog.Profiles.Values, current.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1731"));
        Assert.Null(FdaAccumulativeStfRule.Inspect(fixture.Packages, fixture.Lifecycle,
            new(catalog, current.Assets, current.NodeSchema), 20, default));
    }

    [Fact]
    public async Task ActualFileInspectorAndReportExposeTheTargetFailure()
    {
        var fixture = await Fixture("repeated");
        foreach (var input in fixture.Packages.Sequences.Values.Cast<Capture>()) input.ForbidReads = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context)
            .InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.Files.Checks, check => check.RuleId == "FDA47-1731").Status);
        var report = delivery.CreateReport(PackageValidationMode.Development);
        Assert.Equal(CheckStatus.Fail, Assert.Single(report.Coverage, row => row.RuleId == "FDA47-1731").CheckStatus);
    }

    internal static async Task<(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context)> Fixture(string scenario, string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario is "missing-spec" or "missing-criteria")
        {
            var hash = scenario == "missing-spec" ? "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da" : "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var old = new Capture(app, "0000", "new", null, Ref("0000"), scenario is "unrelated-history" or "multiple-repeat-owners" ? scenario : "valid");
        var middle = new Capture(app, "0001", "append", "0000", scenario == "historical-violation" ? Ref("0000") :
            scenario == "reverted-tag" ? Ref("0000", "synopsis") :
            scenario == "predecessor-bad-reference" ? Ref("0001") + Ref("0001").Replace("#content", "#missing", StringComparison.Ordinal) :
            scenario == "property-order" ? Ref("0001", properties: "<property name='a' info-type='us'>1</property><property name='b' info-type='us'>2</property>") :
            Ref("0001"), scenario == "historical-delete" ? "delete-old" : scenario == "multiple-repeat-owners" ? scenario : "valid");
        var references = scenario switch
        {
            "repeated" or "changed-study-id" or "multiple-owner" or "current-delete" or "multiple-repeat-owners" or "new-with-modified" => Ref("0001"),
            "ancestor" or "reverted-tag" or "historical-delete" => Ref("0000"),
            "identifier-only" => "",
            "changed-tag" => Ref("0001", "synopsis"),
            "changed-block-property" => Ref("0001", blockProperty: "changed"),
            "independent" => Ref("0000"),
            "target-bad-reference" => Ref("0001") + Ref("0002").Replace("#content", "#missing", StringComparison.Ordinal),
            "predecessor-bad-reference" => Ref("0001"),
            "property-order" => Ref("0001", properties: "<property name='b' info-type='us'>2</property><property name='a' info-type='us'>1</property>"),
            _ => Ref("0002")
        };
        var target = new Capture(app, "0002", scenario is "independent" or "first" or "historical-stf-unavailable" or "historical-stf-hash" ? "new" : "append",
            scenario is "independent" or "first" or "historical-stf-unavailable" or "historical-stf-hash" ? null : "0001", references, scenario == "unrelated-history" ? "valid" : scenario);
        Capture[] history = scenario is "missing-history" or "first" or "historical-stf-unavailable" ? [] : [old, middle];
        var packages = new PackageInputSet(target, new(app, "0002", history.Select(input => new HistoryBaselineEntry(
            input.Manifest.SequenceNumber, HistorySourceKind.ImportedExternal, input.SourceId, input.InputDigest,
            PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified))), history);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
            Assert.NotEmpty(xml.StudyTaggingFiles.Documents);
            Assert.All(xml.StudyTaggingFiles.Documents, stf =>
            {
                Assert.True(stf.ReadComplete);
                Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
            });
        }
        foreach (var input in packages.Sequences.Values.Cast<Capture>()) input.ForbidReads = true;
        if (scenario == "historical-stf-hash")
        {
            var changedXml = lifecycle.XmlInspections.Select(xml => xml.InputDigest != old.InputDigest ? xml :
                new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
                    xml.Backbones, xml.Checks, xml.RulesDigest, new PackageStfInspection(xml.StudyTaggingFiles.InventoryComplete,
                        xml.StudyTaggingFiles.Documents.Select(document => new ParsedStudyTaggingFile(document.LogicalPath, new string('a', 64),
                            document.ReadComplete, document.DtdStatus, document.SystemIdentifier, document.Elements, document.ProcessingInstructions, document.ResolvedAssets)))));
            lifecycle = new(packages, lifecycle.ProfileSnapshotId, changedXml, lifecycle.Events, lifecycle.States, lifecycle.Checks,
                context.Catalog.Digest, context.Assets.Digest);
        }
        return (packages, lifecycle, context);
    }

    private static string Ref(string sequence, string tag = "study-report-body", string blockProperty = "same", string properties = "") =>
        "<content-block><block-title>Documents</block-title><property name='group' info-type='us'>" + blockProperty + "</property>" +
        "<doc-content xlink:href='../../../" + sequence + "/index.xml#content'>" + properties + "<file-tag name='" + tag + "' info-type='ich'/></doc-content></content-block>";

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        internal Capture(Guid app, string sequence, string operation, string? previous, string references, string scenario = "valid")
        {
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            {
                using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray();
            }, StringComparer.Ordinal);
            var modified = previous is null ? "" : " modified-file='../" + previous + "/index.xml#stf'";
            var pdf = scenario == "reuse-pdf" ? "../0000/m4/study/content.pdf" : "m4/study/content.pdf";
            var extraOwner = scenario == "multiple-owner" ? Leaf("other-stf", "new", "m4/study/stf.xml") :
                scenario == "multiple-repeat-owners" ? Leaf("other-stf", operation, "m4/study/stf.xml", modified.Replace("#stf", "#other-stf", StringComparison.Ordinal)) :
                scenario == "unrelated-history" ? Leaf("other-stf", "new", "m4/study/stf-b.xml") : "";
            var deletion = scenario is "delete-old" or "current-delete" ? "<leaf ID='deleted' operation='delete' checksum='' checksum-type='md5' modified-file='../" +
                (scenario == "delete-old" ? "0000" : "0001") + "/index.xml#content'><title>Deleted</title></leaf>" : "";
            var contentOperation = scenario == "current-append" ? "append" : scenario == "current-replace" ? "replace" : "new";
            var contentModified = contentOperation == "new" && scenario != "new-with-modified" ? "" : " modified-file='../0001/index.xml#content'";
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:ectd SYSTEM 'util/dtd/ich-ectd-3-2.dtd'>" +
                "<ectd:ectd xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3c.org/1999/xlink' dtd-version='3.2'>" +
                "<m1-administrative-information-and-prescribing-information>" + Leaf("regional", "new", "m1/us/us-regional.xml") + "</m1-administrative-information-and-prescribing-information>" +
                "<m4-nonclinical-study-reports><m4-3-literature-references>" + Leaf("stf", operation, scenario is "historical-stf-unavailable" or "historical-stf-hash" ? "../0000/m4/study/stf.xml" : "m4/study/stf.xml", modified) +
                extraOwner + Leaf("content", contentOperation, pdf, contentModified) + deletion + "</m4-3-literature-references></m4-nonclinical-study-reports></ectd:ectd>");
            _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
                "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>Study</title><study-id>" +
                (scenario == "changed-study-id" ? "Corrected" : "A") + "</study-id></study-identifier><study-document>" + references + "</study-document></ectd:study>");
            _files[sequence + "/m4/study/content.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            if (scenario == "unrelated-history")
                _files[sequence + "/m4/study/stf-b.xml"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(_files[sequence + "/m4/study/stf.xml"])
                    .Replace("#content", "#missing", StringComparison.Ordinal).Replace("<study-id>A</study-id>", "<study-id>B</study-id>", StringComparison.Ordinal));
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Leaf(string id, string operation, string href, string extra = "") => "<leaf ID='" + id + "' operation='" + operation +
            "' checksum='00000000000000000000000000000000' checksum-type='md5' xlink:href='" + href + "'" + extra + "><title>" + id + "</title></leaf>";
        public bool ForbidReads { get; set; }
        public PackageInputManifest Manifest { get; }
        public string SourceId => "accumulative-stf-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => ForbidReads ? throw new InvalidOperationException("No content reread permitted.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => ForbidReads ? throw new InvalidOperationException("Only frozen facts are allowed.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
