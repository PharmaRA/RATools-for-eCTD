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

public sealed class FdaStfTitleMatchRuleTests
{
    private const string RuleId = "FDA47-1953";

    [Theory]
    [InlineData("Study A", "Study A", "valid", CheckStatus.Pass, 0)]
    [InlineData("Study A", "Study B", "valid", CheckStatus.Fail, 1)]
    [InlineData("Study A", "study a", "valid", CheckStatus.Fail, 1)]
    [InlineData("Study A", " Study A ", "valid", CheckStatus.Fail, 1)]
    [InlineData("", "", "valid", CheckStatus.Pass, 0)]
    [InlineData(" ", " ", "valid", CheckStatus.Pass, 0)]
    [InlineData("", "A", "valid", CheckStatus.Fail, 1)]
    [InlineData("A & B", "A & B", "char-reference", CheckStatus.Pass, 0)]
    [InlineData("A", "A", "doc-title", CheckStatus.Pass, 0)]
    [InlineData("A", "B", "doc-title", CheckStatus.Fail, 1)]
    [InlineData("A", "B", "unreferenced", CheckStatus.NotApplicable, 0)]
    [InlineData("A", "B", "delete", CheckStatus.NotApplicable, 0)]
    [InlineData("A", "B", "tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "index-tail", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "missing-title", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "wrong-root", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "missing", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "ordinary", CheckStatus.NotApplicable, 0)]
    [InlineData("A", "A", "history", CheckStatus.Pass, 0)]
    [InlineData("A", "B", "history", CheckStatus.Fail, 1)]
    [InlineData("A", "B", "history-only", CheckStatus.NotApplicable, 0)]
    [InlineData("A", "B", "history-missing", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "history-missing-nonxml", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "history-fragment", CheckStatus.NotEvaluated, 0)]
    [InlineData("A", "B", "two-owners", CheckStatus.Fail, 2)]
    [InlineData("A", "B", "mixed-owners", CheckStatus.Fail, 1)]
    public async Task ComparesResearchTitleWithEachExactCurrentOwner(string study, string leaf, string scenario, CheckStatus expected, int count)
    {
        var fixture = await Fixture(study, leaf, scenario);
        var check = Inspect(fixture)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        foreach (var finding in check.Findings)
        {
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal(study, finding.Expected);
            Assert.Equal(leaf, finding.Actual);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == (scenario == "history" ? "0000" : "0001") + "/m4/study/stf-A.xml");
        }
        if (scenario == "mixed-owners") Assert.Equal("owner", Assert.Single(check.Findings).Location.LeafId);
        if (count == 2) Assert.NotEqual(check.Findings[0].Location.LeafId, check.Findings[1].Location.LeafId);
    }

    [Theory]
    [InlineData("derived-title")]
    [InlineData("equal-derived-title")]
    [InlineData("derived-delete")]
    [InlineData("address")]
    [InlineData("address-application")]
    [InlineData("address-backbone")]
    [InlineData("address-id")]
    [InlineData("backbone-profile")]
    [InlineData("unlisted-owner-title")]
    [InlineData("unlisted-study-title")]
    [InlineData("missing-owner-title-element")]
    [InlineData("missing-study-title-element")]
    [InlineData("hash")]
    [InlineData("equal-hash")]
    [InlineData("context")]
    [InlineData("equal-context")]
    public async Task ConflictingOwnerOrTitleEvidenceCannotProduceComparison(string variant)
    {
        var fixture = await Fixture("A", variant.StartsWith("equal", StringComparison.Ordinal) ? "A" : "B");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Elements, element => element.Attribute("ID") == "owner");
        var elements = index.Elements.ToList();
        if (variant == "unlisted-owner-title")
        {
            var title = Assert.Single(elements, element => element.ParentPath == owner.NodePath && element.LocalName == "title");
            elements[elements.FindIndex(element => element.NodePath == owner.NodePath)] = new(owner.NodePath, owner.ParentPath, owner.Name, owner.LocalName,
                owner.NamespaceUri, owner.Attributes.Values, owner.Children.Where(path => path != title.NodePath), owner.Text, owner.Location);
        }
        if (variant == "missing-owner-title-element") elements = elements.Where(element => element.ParentPath != owner.NodePath || element.LocalName != "title").ToList();
        if (variant == "backbone-profile") Assert.NotEqual(PackageValidationCatalog.IchProfile, index.ProfileSnapshotId);
        var changedIndex = new ParsedBackbone(index.LogicalPath, variant == "backbone-profile" ? PackageValidationCatalog.IchProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes,
            index.Leaves.Select(item => item.Id != "owner" ? item : variant switch
            {
                "derived-title" or "equal-derived-title" => item with { Title = "C" }, "derived-delete" => item with { Operation = "delete" },
                "address" => item with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0000", "index.xml", "owner") },
                "address-application" => item with { Address = new(Guid.NewGuid(), "0001", "index.xml", "owner") },
                "address-backbone" => item with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0001", "other.xml", "owner") },
                "address-id" => item with { Address = new(fixture.Packages.Target.Manifest.ApplicationId, "0001", "index.xml", "other") }, _ => item
            }), index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        var stf = Assert.Single(xml.StudyTaggingFiles.Documents);
        var stfElements = stf.Elements.ToList();
        if (variant == "unlisted-study-title")
        {
            var identifier = Assert.Single(stfElements, element => element.LocalName == "study-identifier");
            var title = Assert.Single(stfElements, element => element.ParentPath == identifier.NodePath && element.LocalName == "title");
            stfElements[stfElements.FindIndex(element => element.NodePath == identifier.NodePath)] = new(identifier.NodePath, identifier.ParentPath,
                identifier.Name, identifier.LocalName, identifier.NamespaceUri, identifier.Attributes.Values, identifier.Children.Where(path => path != title.NodePath), identifier.Text, identifier.Location);
        }
        if (variant == "missing-study-title-element") stfElements = stfElements.Where(element => element.LocalName != "title").ToList();
        var document = new ParsedStudyTaggingFile(stf.LogicalPath, variant is "hash" or "equal-hash" ? new string('f', 64) : stf.CapturedSha256,
            stf.ReadComplete, stf.DtdStatus, stf.SystemIdentifier, stfElements, stf.ProcessingInstructions, stf.ResolvedAssets);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(item => item.Kind != BackboneKind.Ich).Append(changedIndex), xml.Checks,
            variant is "context" or "equal-context" ? new string('f', 64) : xml.RulesDigest, new(xml.StudyTaggingFiles.InventoryComplete, [document])));
        var check = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("A", false)]
    [InlineData("B", false)]
    [InlineData("A", true)]
    [InlineData("B", true)]
    public async Task DetachedOwnerCannotEstablishCurrentTitlePair(string leafTitle, bool cycle)
    {
        var fixture = await Fixture("A", leafTitle);
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        var owner = Assert.Single(index.Elements, element => element.Attribute("ID") == "owner");
        var elements = index.Elements.Select(element => element.NodePath == owner.NodePath ?
            new ParsedXmlElement(element.NodePath, cycle ? owner.NodePath : null, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values, cycle ? element.Children.Append(owner.NodePath) : element.Children, element.Text, element.Location) :
            element.NodePath == owner.ParentPath ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri,
                element.Attributes.Values, element.Children.Where(path => path != owner.NodePath), element.Text, element.Location) : element).ToArray();
        Assert.Equal(cycle ? 1 : 2, elements.Count(element => element.ParentPath is null));
        var altered = new ParsedBackbone(index.LogicalPath, index.ProfileSnapshotId, index.Kind, index.ReadComplete, index.DtdStatus,
            index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier, elements, index.Nodes,
            index.Leaves.Select(leaf => leaf.Id == "owner" ? leaf with { ParentNodePath = cycle ? owner.NodePath : null } : leaf),
            index.IdIndex.Values.SelectMany(ids => ids), index.ResolvedAssets, index.ProcessingInstructions);
        fixture = ReplaceXml(fixture, new(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Where(document => document.Kind != BackboneKind.Ich).Append(altered), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        var check = Inspect(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("valid", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("valid", "2013-04-30", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-23", CheckStatus.NotEvaluated)]
    public async Task SourceAndDateStayBound(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Inspect(await Fixture("A", "B", variant: variant, date: date))!.Status);

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(2, CheckStatus.Fail)]
    public async Task PerOwnerFailuresShareBudgetAndImmutableProgress(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("A", "B", "two-owners");
        var snapshots = new List<RuleCheckResult>();
        var result = Inspect(fixture, budget, check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); })!;
        Assert.Equal(expected, result.Status);
        Assert.Equal(budget, result.Findings.Count);
        if (budget > 0) Assert.Single(snapshots[0].Findings);
        if (budget == 2) Assert.Equal(2, snapshots[1].Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsPendingOrKnownMismatch(bool afterFailure)
    {
        var fixture = await Fixture("A", "B");
        using var cancel = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, progress: check =>
        { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancel.Cancel(); }, token: cancel.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Fact]
    public async Task EqualTitlesDoNotSpendFindingBudget()
        => Assert.Equal(CheckStatus.Pass, Inspect(await Fixture("A", "A"), budget: 0)!.Status);

    [Fact]
    public async Task AlreadyCancelledTokenPropagates()
    {
        var fixture = await Fixture("A", "A");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Inspect(fixture, token: cancel.Token));
    }

    [Theory]
    [InlineData("A", "valid", CheckStatus.Pass)]
    [InlineData("B", "valid", CheckStatus.Fail)]
    [InlineData("B", "unreferenced", CheckStatus.NotApplicable)]
    public async Task DeliveryAndReportExposeTheTitlePair(string leaf, string scenario, CheckStatus expected)
    {
        var fixture = await Fixture("A", leaf, scenario);
        Assert.Equal(ImplementationStatus.Implemented, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        foreach (var capture in fixture.Packages.Sequences.Values.Cast<Capture>()) capture.Armed = false;
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task OldCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("A", "B");
        var context = fixture.Context;
        var catalog = new ValidationRuleCatalog(context.Catalog.Version, new string('a', 64), context.Catalog.Sources.Values, context.Catalog.Profiles.Values,
            context.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != RuleId));
        Assert.Null(Inspect(fixture with { Context = new(catalog, context.Assets, context.NodeSchema) }));
    }

    private static RuleCheckResult? Inspect(FixtureData fixture, int budget = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaStfTitleMatchRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, token, progress);
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static FixtureData ReplaceXml(FixtureData fixture, PackageXmlInspection changed) => fixture with
    { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(xml => xml.InputDigest != changed.InputDigest).Append(changed),
        fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };

    private static async Task<FixtureData> Fixture(string study, string leaf, string scenario = "valid", string variant = "valid", string? date = "2026-11-23")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (variant == "missing-source")
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
            Assert.DoesNotContain(snapshot.Resources, resource => resource.Sha256 == hash);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", study, leaf, scenario);
        var history = scenario is "history" or "history-only" or "history-fragment" ? new Capture(app, "0000", study, "Old owner", "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] : [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]), history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == target.InputDigest);
        var index = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.Ich);
        if (scenario == "index-tail") Assert.False(index.ReadComplete);
        else
        {
            Assert.Equal(CheckStatus.Pass, index.DtdStatus);
            if (scenario is "unreferenced" or "history-only") Assert.DoesNotContain(index.Leaves, item => item.Id == "owner");
            else Assert.Equal(leaf, Assert.Single(index.Leaves, item => item.Id == "owner").Title);
        }
        var selected = history is null ? xml : Assert.Single(lifecycle.XmlInspections, item => item.InputDigest == history.InputDigest);
        if (scenario is "missing" or "history-missing" or "history-missing-nonxml" or "ordinary") Assert.Empty(selected.StudyTaggingFiles.Documents);
        else
        {
            var stf = Assert.Single(selected.StudyTaggingFiles.Documents);
            if (scenario == "tail")
            {
                Assert.False(stf.ReadComplete);
                var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
                Assert.Equal(study, Assert.Single(stf.Elements, element => element.ParentPath == identifier.NodePath && element.LocalName == "title").Text);
            }
            else if (scenario is "wrong-root" or "missing-title") Assert.NotEqual(CheckStatus.Pass, stf.DtdStatus);
            else
            {
                Assert.True(stf.ReadComplete);
                Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
                var identifier = Assert.Single(stf.Elements, element => element.LocalName == "study-identifier");
                Assert.Equal(study, Assert.Single(stf.Elements, element => element.ParentPath == identifier.NodePath && element.LocalName == "title").Text);
            }
        }
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        if (scenario == "history-missing-nonxml")
        {
            Assert.Empty(packages.Baseline.Entries);
            Assert.Equal("../0000/m4/study/stf-A.txt", Assert.Single(index.Elements, element => element.Attribute("ID") == "owner").Attribute("href", "http://www.w3c.org/1999/xlink"));
            Assert.True(PackageStfBindingReader.Read(packages, lifecycle, context, default).TargetScopeComplete);
        }
        return new(packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string study, string leafTitle, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var name = scenario == "ordinary" ? "metadata.xml" : scenario == "history-missing-nonxml" ? "stf-A.txt" : "stf-A.xml";
            var section = new XElement("m4-3-literature-references", Leaf("report", "m4/study/report.pdf", "Document title"));
            if (scenario is not ("unreferenced" or "history-only"))
            {
                var href = (scenario.StartsWith("history", StringComparison.Ordinal) ? "../0000/" : "") + "m4/study/" + name + (scenario == "history-fragment" ? "#bad" : "");
                section.Add(Leaf("owner", href, leafTitle, scenario == "delete" ? "delete" : "new"));
                if (scenario is "two-owners" or "mixed-owners") section.Add(Leaf("owner2", href, scenario == "mixed-owners" ? study : leafTitle));
            }
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml", "Regional")), new XElement("m4-nonclinical-study-reports", section));
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString();
            if (scenario == "index-tail") index = index[..^8];
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index);
            var title = scenario == "missing-title" ? "" : scenario == "char-reference" ? "<title>A &#38; B</title>" : new XElement("title", study).ToString(SaveOptions.DisableFormatting);
            const string declaration = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>";
            var doc = scenario == "doc-title" ? "<doc-content xlink:href='../../index.xml#report'><title>Unrelated document title</title></doc-content>" : "";
            var stf = declaration + "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink' dtd-version='2.2'><study-identifier>" + title + "<study-id>A</study-id></study-identifier><study-document>" + doc + "</study-document></ectd:study>";
            if (scenario == "tail") stf = stf[..^8];
            if (scenario == "wrong-root") stf = declaration + "<other/>";
            if (scenario == "ordinary") stf = "<ordinary/>";
            if (!scenario.StartsWith("history", StringComparison.Ordinal) && scenario != "missing") _files[sequence + "/m4/study/" + name] = Encoding.UTF8.GetBytes(stf);
            _files[sequence + "/m4/study/report.pdf"] = "%PDF-1.4 fixture"u8.ToArray();
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string title, string operation = "new") => new("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", title));
        public bool Armed { get; set; }
        public string SourceId => "fda1953-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => Armed ? throw new InvalidOperationException("Use frozen XML only.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Armed ? throw new InvalidOperationException("Use frozen identity only.") : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
