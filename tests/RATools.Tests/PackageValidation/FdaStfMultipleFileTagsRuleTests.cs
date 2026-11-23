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

public sealed class FdaStfMultipleFileTagsRuleTests
{
    private const string RuleId = "FDA47-1935";
    private const string Tag = "<file-tag name='study-report-body' info-type='ich'/>";
    private const string Doc = "<doc-content xlink:href='../../index.xml#report'>";

    [Theory]
    [InlineData("none", "valid", CheckStatus.Pass, 0)]
    [InlineData("one", "valid", CheckStatus.Pass, 0)]
    [InlineData("two", "valid", CheckStatus.Fail, 1)]
    [InlineData("different", "valid", CheckStatus.Fail, 1)]
    [InlineData("three", "valid", CheckStatus.Fail, 1)]
    [InlineData("block-one", "valid", CheckStatus.Pass, 0)]
    [InlineData("block-only", "valid", CheckStatus.Pass, 0)]
    [InlineData("block-two", "valid", CheckStatus.Fail, 1)]
    [InlineData("siblings", "valid", CheckStatus.Pass, 0)]
    [InlineData("mixed", "valid", CheckStatus.Fail, 1)]
    [InlineData("two-docs", "valid", CheckStatus.Fail, 2)]
    [InlineData("empty", "valid", CheckStatus.NotApplicable, 0)]
    [InlineData("two", "tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("one", "tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("two", "wrong-root", CheckStatus.NotEvaluated, 0)]
    [InlineData("two", "unreferenced", CheckStatus.Fail, 1)]
    [InlineData("two", "history", CheckStatus.Fail, 1)]
    [InlineData("one", "history", CheckStatus.Pass, 0)]
    [InlineData("two", "history-only", CheckStatus.NotApplicable, 0)]
    [InlineData("two", "history-missing", CheckStatus.NotEvaluated, 0)]
    [InlineData("two", "history-missing-nonxml", CheckStatus.NotEvaluated, 0)]
    [InlineData("two", "history-fragment", CheckStatus.NotEvaluated, 0)]
    [InlineData("two", "missing", CheckStatus.NotEvaluated, 0)]
    [InlineData("two", "ordinary", CheckStatus.NotApplicable, 0)]
    public async Task CountsDirectElementsPerOccurrenceNotAncestorsOrSharedHref(string body, string scenario, CheckStatus expected, int findings)
    {
        var fixture = await Fixture(body, scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(findings, result.Findings.Count);
        foreach (var finding in result.Findings)
        {
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal((scenario == "history" ? "0000" : "0001") + "/m4/study/stf-A.xml", finding.Location.LogicalPath);
            Assert.Equal("file-tag", finding.Location.FieldPath);
            Assert.Equal("At most 1 direct file-tag", finding.Expected);
            Assert.Single(finding.Location.Files!);
        }
        if (body == "mixed")
        {
            var docs = Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).StudyTaggingFiles.Documents).Elements.Where(element => element.LocalName == "doc-content").ToArray();
            Assert.Equal(docs[1].NodePath, Assert.Single(result.Findings).Location.NodePath);
        }
        if (findings == 2) Assert.NotEqual(result.Findings[0].Location.NodePath, result.Findings[1].Location.NodePath);
    }

    [Theory]
    [InlineData("hash", "two")]
    [InlineData("hash", "one")]
    [InlineData("context", "two")]
    [InlineData("context", "one")]
    [InlineData("duplicate", "two")]
    [InlineData("unlisted", "two")]
    [InlineData("missing-element", "two")]
    [InlineData("wrong-parent", "two")]
    public async Task FrozenIdentityAndBidirectionalChildrenCannotBeContradictory(string variant, string body)
    {
        var fixture = await Fixture(body);
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var doc = Assert.Single(stf.Elements, element => element.LocalName == "doc-content");
        var tag = stf.Elements.First(element => element.LocalName == "file-tag");
        var elements = stf.Elements.Where(element => variant != "missing-element" || element.NodePath != tag.NodePath).Select(element =>
            variant == "unlisted" && element.NodePath == doc.NodePath ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name,
                element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children.Where(path => path != tag.NodePath), element.Text, element.Location) :
            variant == "wrong-parent" && element.NodePath == tag.NodePath ? new ParsedXmlElement(element.NodePath, "/wrong", element.Name,
                element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location) : element);
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks,
            variant == "context" ? new string('f', 64) : xml.RulesDigest, new(true, variant == "duplicate" ? [document, document] : [document])));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("profile")]
    public async Task HistoricalIdentityMismatchCannotBecomeAbsentScope(string variant)
    {
        var fixture = await Fixture("two", "history");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest != fixture.Packages.Target.InputDigest);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant == "hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, variant == "profile" ? PackageValidationCatalog.IchProfile : xml.ProfileSnapshotId,
            xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks, xml.RulesDigest, new(true, [document])));
        Assert.Equal(CheckStatus.NotEvaluated, Inspect(fixture)!.Status);
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("application")]
    [InlineData("backbone")]
    [InlineData("leaf-id")]
    [InlineData("profile")]
    public async Task HistoricalSelectionRequiresExactCurrentOwnerIdentity(string variant)
    {
        var fixture = await Fixture("two", "history");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Leaves, leaf => leaf.Id == "owner");
        var address = Assert.IsType<RATools.Domain.Ctd.LeafAddress>(owner.Address);
        var altered = new RATools.Domain.Ctd.LeafAddress(variant == "application" ? Guid.NewGuid() : address.ApplicationId,
            variant == "sequence" ? "0000" : address.SequenceNumber, variant == "backbone" ? "other.xml" : address.BackboneRelativePath,
            variant == "leaf-id" ? "other" : address.LeafId);
        var changedIndex = new ParsedBackbone(index.LogicalPath, variant == "profile" ? PackageValidationCatalog.IchProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, index.Elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id == "owner" ? leaf with { Address = altered } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(changedIndex), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        var bound = Assert.Single(PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default).Documents);
        Assert.True(Assert.Single(bound.Owners, item => item.BackboneLogicalPath == "0001/index.xml").Qualified);
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task EndToEndWorkBudgetCannotBecomePass()
    {
        var fixture = await Fixture("one");
        var capture = Assert.IsType<Capture>(fixture.Packages.Target);
        capture.Limits = new() { MaxXmlNodes = 1 };
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, capture.Limits.Digest(),
            xml.Backbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        Assert.Equal(CheckStatus.NotEvaluated, Inspect(fixture)!.Status);
    }

    [Fact]
    public async Task AlreadyCancelledTokenPropagates()
    {
        var fixture = await Fixture("one");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token));
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task SourcesAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("two", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task EachViolatingDocSpendsOneFindingAndKeepsEarlierSnapshots(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-docs");
        var progress = new List<RuleCheckResult>();
        var result = Inspect(fixture, budget, check => { if (check.Status == CheckStatus.Fail) progress.Add(check); })!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(progress[0].Findings);
        if (budget == 2) Assert.Equal(2, progress[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownMultiplicity(bool afterFailure)
    {
        var fixture = await Fixture("two");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: result =>
        { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("one", CheckStatus.Pass)]
    [InlineData("two", CheckStatus.Fail)]
    [InlineData("empty", CheckStatus.NotApplicable)]
    public async Task DeliveryAndReportExposeMultiplicityIndependently(string body, CheckStatus expected)
    {
        var fixture = await Fixture(body);
        Assert.Equal(ImplementationStatus.Implemented, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowNewRule()
    {
        var fixture = await Fixture("two");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfMultipleFileTagsRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static FixtureData ReplaceXml(FixtureData fixture, PackageXmlInspection changed) => fixture with
    { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != changed.InputDigest).Append(changed),
        fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };

    private static async Task<FixtureData> Fixture(string body, string scenario = "valid", string variant = "valid", string? date = "2026-11-23")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant is "missing-source" or "missing-spec")
        {
            var hash = variant == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da";
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
            Assert.DoesNotContain(snapshot.Resources, resource => resource.Sha256 == hash);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", body, scenario);
        var history = scenario is "history" or "history-only" or "history-fragment" ? new Capture(app, "0000", body, "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
        var selected = history is null ? xml : Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == history.InputDigest);
        if (scenario is "missing" or "history-missing" or "history-missing-nonxml" or "ordinary") Assert.Empty(selected.StudyTaggingFiles.Documents);
        else
        {
            var stf = Assert.Single(selected.StudyTaggingFiles.Documents);
            if (scenario == "wrong-root") Assert.DoesNotContain(stf.Elements, element => element.ParentPath is null && element.LocalName == "study");
            else
            {
                Assert.Equal(scenario != "tail", stf.ReadComplete);
                if (scenario != "tail") Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
                var docs = stf.Elements.Where(element => element.LocalName == "doc-content").ToArray();
                Assert.Equal(body is "siblings" or "mixed" or "two-docs" ? 2 : body == "empty" ? 0 : 1, docs.Length);
                var counts = docs.Select(doc => stf.Elements.Count(element => element.ParentPath == doc.NodePath && element.LocalName == "file-tag")).ToArray();
                Assert.Equal(body switch { "none" or "block-only" => [0], "one" or "block-one" => [1], "three" => [3], "siblings" => [1, 1], "mixed" => [1, 2], "two-docs" => [2, 2], "empty" => Array.Empty<int>(), _ => [2] }, counts);
            }
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string body, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var name = scenario == "ordinary" ? "metadata.xml" : scenario == "history-missing-nonxml" ? "stf-A.txt" : "stf-A.xml";
            var section = new XElement("m4-3-literature-references", Leaf("report", "m4/study/report.pdf"));
            if (scenario is not ("unreferenced" or "history-only")) section.Add(Leaf("owner", (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name + (scenario == "history-fragment" ? "#bad" : "")));
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")), new XElement("m4-nonclinical-study-reports", section));
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            var none = Doc + "</doc-content>"; var one = Doc + Tag + "</doc-content>"; var two = Doc + Tag + Tag + "</doc-content>";
            const string block = "<content-block><block-title>Block</block-title>";
            var content = body switch
            {
                "none" => none, "one" => one, "two" => two, "different" => Doc + Tag + "<file-tag name='synopsis' info-type='ich'/></doc-content>",
                "three" => Doc + Tag + Tag + Tag + "</doc-content>", "block-one" => block + Tag + one + "</content-block>",
                "block-only" => block + Tag + Tag + none + "</content-block>", "block-two" => block + two + "</content-block>",
                "siblings" => one + one, "mixed" => one + two, "two-docs" => two + two, "empty" => "", _ => throw new ArgumentOutOfRangeException(nameof(body))
            };
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document>" + content + "</study-document></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public bool Armed { get; set; }
        public string SourceId => "fda1935-" + Manifest.SequenceNumber;
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
