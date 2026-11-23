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

public sealed class FdaStfTocLocationRuleTests
{
    private const string RuleId = "FDA47-1901";

    [Theory]
    [InlineData("m2", "valid", CheckStatus.Fail)]
    [InlineData("m3", "valid", CheckStatus.Fail)]
    [InlineData("m2", "extension", CheckStatus.Fail)]
    [InlineData("m4", "valid", CheckStatus.NotEvaluated)]
    [InlineData("m5", "valid", CheckStatus.NotEvaluated)]
    [InlineData("m4", "extension", CheckStatus.NotEvaluated)]
    [InlineData("m2", "tail", CheckStatus.Fail)]
    [InlineData("m2", "wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("m2", "missing-root", CheckStatus.NotEvaluated)]
    [InlineData("m2", "missing", CheckStatus.NotEvaluated)]
    [InlineData("m2", "unreferenced", CheckStatus.NotApplicable)]
    [InlineData("m2", "ordinary", CheckStatus.NotApplicable)]
    [InlineData("m2", "delete", CheckStatus.NotApplicable)]
    [InlineData("m2", "history", CheckStatus.Fail)]
    [InlineData("m2", "history-tail", CheckStatus.Fail)]
    [InlineData("m4", "history", CheckStatus.NotEvaluated)]
    [InlineData("m2", "history-only", CheckStatus.NotApplicable)]
    [InlineData("m2", "history-missing", CheckStatus.NotEvaluated)]
    [InlineData("m2", "history-fragment", CheckStatus.NotEvaluated)]
    [InlineData("m1", "valid", CheckStatus.NotEvaluated)]
    [InlineData("m2", "index-tail", CheckStatus.NotEvaluated)]
    public async Task RealOwnerLocationDoesNotBorrowPhysicalFolderOrHistoricalPlacement(string module, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(module, scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal("owner", finding.Location.LeafId);
            Assert.StartsWith(module, finding.Actual);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == (scenario.StartsWith("history", StringComparison.Ordinal) ? "0000" : "0001") + "/m4/study/stf-A.xml");
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("derived-delete")]
    [InlineData("raw-default")]
    [InlineData("broken-parent")]
    [InlineData("hash")]
    [InlineData("duplicate")]
    [InlineData("context")]
    [InlineData("backbone-profile")]
    public async Task ContradictoryFrozenFactsCannotProduceLocationFindings(string variant)
    {
        var fixture = await Fixture("m2");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, backbone => backbone.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Elements, element => element.Attribute("ID") == "owner");
        var elements = index.Elements.Select(element => variant == "raw-default" && element.NodePath == owner.NodePath ?
            new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values.Select(attribute => attribute.Name == "operation" ? attribute with { IsDefault = true, ExplicitValue = null } : attribute), element.Children, element.Text, element.Location) :
            variant == "broken-parent" && element.NodePath == owner.ParentPath ?
            new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values,
                element.Children.Where(child => child != owner.NodePath), element.Text, element.Location) : element);
        var changedIndex = new ParsedBackbone(index.LogicalPath, variant == "backbone-profile" ? PackageValidationCatalog.IchProfile : index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes,
            index.Leaves.Select(leaf => variant == "derived-delete" && leaf.Id == "owner" ? leaf with { Operation = "delete" } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        var changed = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(backbone => backbone.Kind != BackboneKind.Ich).Append(changedIndex), xml.Checks,
            variant == "context" ? new string('f', 64) : xml.RulesDigest, new(true, variant == "duplicate" ? [document, document] : [document]));
        fixture = ReplaceXml(fixture, changed);
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task UnreferencedDiscoveryUnknownCannotPolluteFullyExcludedCurrentOwners()
    {
        var fixture = await Fixture("m2", "unreferenced");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, backbone => backbone.Kind == BackboneKind.Ich);
        Assert.Equal("m1/us/us-regional.xml", Assert.Single(index.Leaves).Href);
        var regional = Assert.Single(xml.Backbones, backbone => backbone.Kind != BackboneKind.Ich);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        Assert.Equal(xml.ProfileSnapshotId, regional.ProfileSnapshotId);
        Assert.Empty(regional.Leaves);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var incomplete = new ParsedStudyTaggingFile(stf.LogicalPath, stf.CapturedSha256, false, CheckStatus.NotEvaluated,
            stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones, xml.Checks, xml.RulesDigest, new(false, [incomplete])));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotApplicable, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("profile")]
    public async Task HistoricalIdentityMismatchIsUnknownNotAbsent(string variant)
    {
        var fixture = await Fixture("m2", "history");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest != fixture.Packages.Target.InputDigest);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, variant == "profile" ? PackageValidationCatalog.IchProfile : xml.ProfileSnapshotId,
            xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks, xml.RulesDigest, new(true, [document])));
        Assert.Equal(CheckStatus.NotEvaluated, Inspect(fixture)!.Status);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateStayFrozen(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("m2", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task EachOwnerSharesBudgetAndImmutableProgress(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("m2", "two");
        var progress = new List<RuleCheckResult>();
        var result = Inspect(fixture, budget, check => { if (check.Status == CheckStatus.Fail) progress.Add(check); })!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(progress[0].Findings);
        if (budget == 2) Assert.Equal(2, progress[1].Findings.Count);
    }

    [Fact]
    public async Task ExhaustedWorkBudgetDoesNotBecomeAbsentScope()
    {
        var fixture = await Fixture("m2");
        var capture = Assert.IsType<Capture>(fixture.Packages.Target);
        capture.Limits = new() { MaxXmlNodes = 1 };
        var original = Assert.Single(fixture.Lifecycle.XmlInspections);
        fixture = ReplaceXml(fixture, new(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest,
            capture.Limits.Digest(), original.Backbones, original.Checks, original.RulesDigest, original.StudyTaggingFiles));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task FileDeadlineAfterKnownLocationFailureRetainsIt()
    {
        var fixture = await Fixture("m2");
        var capture = Assert.IsType<Capture>(fixture.Packages.Target);
        capture.Armed = false;
        var clock = new DeadlineClock();
        capture.ExpireAtFinalVerification = clock.Expire;
        var files = await new PackageFileInspector(clock, fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        Assert.True(capture.LocationRuleObserved);
        Assert.True(capture.Expired);
        Assert.Contains(files.Checks.SelectMany(check => check.Findings), finding => finding.Code == "FILE_INSPECTION_TIMEOUT");
        Assert.Single(Assert.Single(files.Checks, check => check.RuleId == RuleId).Findings);
        Assert.Equal(CheckStatus.Fail, Assert.Single(files.Checks, check => check.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("m2");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: result =>
        { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("m2", CheckStatus.Fail)]
    [InlineData("m4", CheckStatus.NotEvaluated)]
    public async Task DeliveryAndPartialReportExposeModuleBoundary(string module, CheckStatus expected)
    {
        var fixture = await Fixture(module);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowNewRule()
    {
        var fixture = await Fixture("m2");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfTocLocationRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static FixtureData ReplaceXml(FixtureData fixture, PackageXmlInspection changed) => fixture with
    { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != changed.InputDigest).Append(changed),
        fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };

    private static async Task<FixtureData> Fixture(string module, string scenario = "valid", string variant = "valid", string? date = "2026-11-23")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", module, scenario);
        var history = scenario is "history" or "history-only" or "history-fragment" or "history-tail" ? new Capture(app, "0000", "m4", scenario == "history-tail" ? "tail" : "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var targetXml = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == target.InputDigest);
        var index = Assert.Single(targetXml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (scenario == "index-tail") Assert.False(index.ReadComplete);
        else Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        if (scenario == "index-tail") Assert.Empty(index.Leaves);
        else if (scenario is "unreferenced" or "history-only") Assert.DoesNotContain(index.Leaves, leaf => leaf.Id == "owner");
        else
        {
            var owner = Assert.Single(index.Leaves, leaf => leaf.Id == "owner");
            Assert.Equal(module == "m1", owner.IsRegionalReference);
            Assert.Equal((scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + (scenario == "ordinary" ? "metadata.xml" : "stf-A.xml") + (scenario == "history-fragment" ? "#bad" : ""), owner.Href);
        }
        if (history is not null)
        {
            var historical = Assert.Single(lifecycle.XmlInspections, xml => xml.InputDigest == history.InputDigest);
            var document = Assert.Single(historical.StudyTaggingFiles.Documents);
            Assert.Equal("0000/m4/study/stf-A.xml", document.LogicalPath);
            Assert.Equal(scenario != "history-tail", document.ReadComplete);
        }
        else if (module == "m1") Assert.False(targetXml.StudyTaggingFiles.InventoryComplete);
        else if (scenario is not ("missing" or "ordinary" or "history-missing"))
        {
            var document = Assert.Single(targetXml.StudyTaggingFiles.Documents);
            if (scenario is "tail") Assert.False(document.ReadComplete);
            else if (scenario is "wrong-root" or "missing-root") Assert.DoesNotContain(document.Elements, element => element.ParentPath is null && element.LocalName == "study");
            else Assert.Equal(CheckStatus.Pass, document.DtdStatus);
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string module, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var region = new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml"));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"), region);
            var heading = module switch { "m2" => "m2-2-introduction", "m3" => "m3-3-literature-references", "m4" => "m4-3-literature-references", _ => "m5-4-literature-references" };
            var container = module switch { "m2" => "m2-common-technical-document-summaries", "m3" => "m3-quality", "m4" => "m4-nonclinical-study-reports", _ => "m5-clinical-study-reports" };
            var section = module == "m1" ? region : new XElement(heading);
            if (module != "m1") root.Add(new XElement(container, section));
            if (scenario == "extension") { var extension = new XElement("node-extension", new XAttribute("ID", "ext"), new XElement("title", "Study")); section.Add(extension); section = extension; }
            if (scenario is not ("unreferenced" or "history-only"))
            {
                var href = (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + (scenario == "ordinary" ? "metadata.xml" : "stf-A.xml") + (scenario == "history-fragment" ? "#bad" : "");
                section.Add(Leaf("owner", href, scenario == "delete" ? "delete" : "new"));
                if (scenario == "two") section.Add(Leaf("owner2", href));
            }
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document/></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "missing-root") stf = declaration;
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing") _files[sequence + "/m4/study/" + (scenario == "ordinary" ? "metadata.xml" : "stf-A.xml")] = Encoding.UTF8.GetBytes(stf);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string operation = "new") => new("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public Action? ExpireAtFinalVerification { get; set; }
        public bool Expired { get; private set; }
        public bool LocationRuleObserved { get; private set; }
        public string SourceId
        {
            get
            {
                if (ExpireAtFinalVerification is not null && new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                    frame.GetMethod()?.DeclaringType == typeof(FdaStfTocLocationRule))) LocationRuleObserved = true;
                return "fda1901-" + Manifest.SequenceNumber;
            }
        }
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; set; } = new();
        public Stream OpenRead(string path) => Armed ? throw new InvalidOperationException("Use frozen XML only.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            if (Armed) throw new InvalidOperationException("Use frozen identity only.");
            if (ExpireAtFinalVerification is { } expire && LocationRuleObserved) { Expired = true; expire(); }
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; return new InertTimer(); }
        public void Expire() => _callback!(_state);
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
