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

public sealed class FdaStfDocContentFileTagRuleTests
{
    private const string RuleId = "FDA47-1918";
    private const string Tag = "<file-tag name='study-report-body' info-type='ich'/>";
    private const string DocStart = "<doc-content xlink:href='../../index.xml#report'>";

    [Theory]
    [InlineData("direct", "valid", CheckStatus.Pass, 0)]
    [InlineData("missing", "valid", CheckStatus.Fail, 1)]
    [InlineData("block-missing", "valid", CheckStatus.Fail, 1)]
    [InlineData("block-tag", "valid", CheckStatus.NotEvaluated, 0)]
    [InlineData("outer-tag", "valid", CheckStatus.NotEvaluated, 0)]
    [InlineData("block-direct", "valid", CheckStatus.Pass, 0)]
    [InlineData("block-both", "valid", CheckStatus.Pass, 0)]
    [InlineData("siblings", "valid", CheckStatus.Fail, 1)]
    [InlineData("two-missing", "valid", CheckStatus.Fail, 2)]
    [InlineData("known-and-inherited", "valid", CheckStatus.Fail, 1)]
    [InlineData("invalid-tag", "valid", CheckStatus.NotEvaluated, 0)]
    [InlineData("empty", "valid", CheckStatus.NotApplicable, 0)]
    [InlineData("missing", "tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("direct", "tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing", "unreferenced", CheckStatus.Fail, 1)]
    [InlineData("missing", "history", CheckStatus.Fail, 1)]
    [InlineData("direct", "history", CheckStatus.Pass, 0)]
    [InlineData("missing", "history-only", CheckStatus.NotApplicable, 0)]
    [InlineData("missing", "history-missing", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing", "history-missing-nonxml", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing", "history-fragment", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing", "missing", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing", "wrong-root", CheckStatus.NotEvaluated, 0)]
    [InlineData("missing", "ordinary", CheckStatus.NotApplicable, 0)]
    public async Task DirectTagsInheritedUncertaintyAndUnreadStructureStayDistinct(string body, string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(body, scenario);
        var result = Inspect(fixture)!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(count, result.Findings.Count);
        foreach (var finding in result.Findings)
        {
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal((scenario == "history" ? "0000" : "0001") + "/m4/study/stf-A.xml", finding.Location.LogicalPath);
            Assert.Contains("doc-content", finding.Location.NodePath!);
            Assert.Equal("file-tag", finding.Location.FieldPath);
            Assert.Single(finding.Location.Files!);
        }
        if (count == 2) Assert.NotEqual(result.Findings[0].Location.NodePath, result.Findings[1].Location.NodePath);
        if (body is "siblings" or "known-and-inherited")
        {
            var stf = Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).StudyTaggingFiles.Documents);
            var docs = stf.Elements.Where(element => element.LocalName == "doc-content").ToArray();
            Assert.Equal(docs[body == "siblings" ? 1 : 0].NodePath, Assert.Single(result.Findings).Location.NodePath);
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("context")]
    [InlineData("duplicate")]
    [InlineData("unlisted-tag")]
    [InlineData("missing-tag-element")]
    [InlineData("direct-hash")]
    [InlineData("direct-context")]
    [InlineData("broken-parent")]
    public async Task ContradictoryFrozenFactsCannotProveOmission(string variant)
    {
        var fixture = await Fixture(variant is "unlisted-tag" or "missing-tag-element" or "direct-hash" or "direct-context" ? "direct" : "missing");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var doc = Assert.Single(stf.Elements, element => element.LocalName == "doc-content");
        var elements = stf.Elements.Where(element => variant != "missing-tag-element" || element.LocalName != "file-tag").Select(element => variant == "broken-parent" && element.NodePath == doc.NodePath ?
            new ParsedXmlElement(element.NodePath, "/unavailable", element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location) : element).ToList();
        if (variant == "unlisted-tag")
        {
            var tag = Assert.Single(elements, element => element.LocalName == "file-tag");
            Assert.Equal("study-report-body", tag.Attribute("name"));
            elements[elements.FindIndex(element => element.NodePath == doc.NodePath)] = new(doc.NodePath, doc.ParentPath, doc.Name, doc.LocalName, doc.NamespaceUri,
                doc.Attributes.Values, doc.Children.Where(path => path != tag.NodePath), doc.Text, doc.Location);
        }
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant is "hash" or "direct-hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, elements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks,
            variant is "context" or "direct-context" ? new string('f', 64) : xml.RulesDigest, new(true, variant == "duplicate" ? [document, document] : [document])));
        var result = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    [InlineData("missing-spec", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task FrozenSourcesAndDateGateTheRule(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("missing", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task EachOmissionSpendsSharedFindingBudgetWithImmutableProgress(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("two-missing");
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
    public async Task CancellationRetainsPendingOrKnownOmission(bool afterFailure)
    {
        var fixture = await Fixture("missing");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: result =>
        { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("block-tag")]
    public async Task WorkBudgetAlsoBoundsDocumentsThatProduceNoFinding(string body)
    {
        var fixture = await Fixture(body);
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
        var fixture = await Fixture("direct");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token));
    }

    [Theory]
    [InlineData("direct", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("missing", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("block-tag", CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    [InlineData("empty", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    public async Task ReportDoesNotUpgradeUnqualifiedInheritance(string body, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture(body);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("missing");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfDocContentFileTagRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
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
            Assert.Contains(snapshot.Resources, item => item.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
            Assert.DoesNotContain(snapshot.Resources, item => item.Sha256 == hash);
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
            if (scenario == "tail")
            {
                Assert.False(stf.ReadComplete);
                Assert.Single(stf.Elements, element => element.LocalName == "doc-content");
                Assert.Equal(body == "direct" ? 1 : 0, stf.Elements.Count(element => element.LocalName == "file-tag"));
            }
            else if (scenario == "wrong-root") Assert.DoesNotContain(stf.Elements, element => element.ParentPath is null && element.LocalName == "study");
            else if (body == "invalid-tag")
            {
                Assert.NotEqual(CheckStatus.Pass, stf.DtdStatus);
                // The reader may reject the malformed tag before retaining its element;
                // this case only proves invalid DTD evidence cannot qualify an omission.
            }
            else
            {
                Assert.True(stf.ReadComplete);
                Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
                var expectedDocs = body is "siblings" or "two-missing" or "known-and-inherited" ? 2 : body == "empty" ? 0 : 1;
                Assert.Equal(expectedDocs, stf.Elements.Count(element => element.LocalName == "doc-content"));
            }
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        if (scenario == "history-missing-nonxml")
        {
            Assert.Single(packages.Sequences);
            var owner = Assert.Single(Assert.Single(xml.Backbones, backbone => backbone.Kind == BackboneKind.Ich).Leaves, leaf => leaf.Id == "owner");
            Assert.Equal("../0000/m4/study/stf-A.txt", owner.Href);
            Assert.True(PackageStfBindingReader.Read(packages, lifecycle, context, default).TargetScopeComplete);
        }
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
            var missing = DocStart + "</doc-content>";
            var direct = DocStart + Tag + "</doc-content>";
            const string block = "<content-block><block-title>Block</block-title>";
            var content = body switch
            {
                "direct" => direct, "missing" => missing, "block-missing" => block + missing + "</content-block>",
                "block-tag" => block + Tag + missing + "</content-block>",
                "outer-tag" => block + Tag + block + missing + "</content-block></content-block>",
                "block-direct" => block + direct + "</content-block>", "block-both" => block + Tag + direct + "</content-block>", "siblings" => block + direct + missing + "</content-block>",
                "two-missing" => missing + missing, "known-and-inherited" => missing + block + Tag + missing + "</content-block>",
                "invalid-tag" => DocStart + "<file-tag/></doc-content>", "empty" => "", _ => throw new ArgumentOutOfRangeException(nameof(body))
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
        public string SourceId => "fda1918-" + Manifest.SequenceNumber;
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
