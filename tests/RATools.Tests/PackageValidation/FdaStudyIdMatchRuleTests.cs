using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStudyIdMatchRuleTests
{
    [Theory]
    [InlineData("match", CheckStatus.Pass)]
    [InlineData("sprefid", CheckStatus.Pass)]
    [InlineData("mismatch", CheckStatus.Fail)]
    [InlineData("case", CheckStatus.Fail)]
    [InlineData("leading-space", CheckStatus.Fail)]
    [InlineData("lower-parameter", CheckStatus.Fail)]
    [InlineData("multiple-match", CheckStatus.Pass)]
    [InlineData("multiple-mismatch", CheckStatus.Fail)]
    [InlineData("matching-id-unknown-value", CheckStatus.Pass)]
    [InlineData("matching-value-unknown-id", CheckStatus.Pass)]
    [InlineData("unknown-optional", CheckStatus.Pass)]
    [InlineData("unknown-irrelevant-value", CheckStatus.Fail)]
    [InlineData("unknown-id", CheckStatus.NotEvaluated)]
    [InlineData("unknown-parameter", CheckStatus.NotEvaluated)]
    [InlineData("unknown-parameter-nonmatching-value", CheckStatus.Fail)]
    [InlineData("unknown-sprefid", CheckStatus.NotEvaluated)]
    [InlineData("tail-match", CheckStatus.Pass)]
    [InlineData("tail-mismatch", CheckStatus.Fail)]
    [InlineData("no-ts", CheckStatus.NotApplicable)]
    [InlineData("missing-file", CheckStatus.NotEvaluated)]
    [InlineData("missing-leaf", CheckStatus.NotEvaluated)]
    [InlineData("unsupported", CheckStatus.NotEvaluated)]
    [InlineData("wrong-member", CheckStatus.NotEvaluated)]
    [InlineData("ts-io", CheckStatus.NotEvaluated)]
    [InlineData("blank-id", CheckStatus.NotEvaluated)]
    [InlineData("whitespace-id", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-id", CheckStatus.NotEvaluated)]
    [InlineData("multiple-ts", CheckStatus.Fail)]
    [InlineData("unrelated-missing", CheckStatus.Pass)]
    [InlineData("known-fail-unknown", CheckStatus.Fail)]
    [InlineData("historical-match", CheckStatus.Pass)]
    [InlineData("historical-mismatch", CheckStatus.Fail)]
    [InlineData("history-absent", CheckStatus.NotEvaluated)]
    [InlineData("reuse-history", CheckStatus.Pass)]
    [InlineData("stf-space", CheckStatus.Fail)]
    [InlineData("encoded-literal", CheckStatus.Pass)]
    [InlineData("no-second-decode", CheckStatus.Fail)]
    [InlineData("duplicate-ts-reference", CheckStatus.Fail)]
    public async Task RealStudyAndTsPairsUseExactFieldLevelOrMatching(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var result = await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Status);
        if (expected != CheckStatus.Fail) Assert.Empty(result.Findings);
        else
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/m4/study/stf.xml", finding.Location.LogicalPath);
            Assert.Contains("study-id", finding.Location.NodePath, StringComparison.Ordinal);
            Assert.Equal(scenario == "stf-space" ? "A " : scenario == "no-second-decode" ? "A&amp;B" : "A", finding.Expected);
            if (scenario == "historical-mismatch")
                Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0000/m4/study/ts.xpt" &&
                    file.SourceId == fixture.Packages.Sequences["0000"].SourceId);
        }
    }

    [Theory]
    [InlineData("m4.3", CheckStatus.Fail)]
    [InlineData("m5.2", CheckStatus.Fail)]
    [InlineData("m5.3.6", CheckStatus.Fail)]
    [InlineData("m5.4", CheckStatus.Fail)]
    [InlineData("m3.3", CheckStatus.NotApplicable)]
    public async Task ExistingOptionalStfsStillHaveIdentityPairs(string section, CheckStatus expected)
    {
        var fixture = await Fixture("mismatch", section: section);
        Assert.Equal(expected, (await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default))!.Status);
    }

    [Theory]
    [InlineData("match", "2023-03-15", CheckStatus.NotApplicable)]
    [InlineData("match", "2023-03-16", CheckStatus.Pass)]
    [InlineData("match", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-guide", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task FrozenSourceAndDateGatesRemainExplicit(string scenario, string? date, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, date);
        Assert.Equal(expected, (await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default))!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task SharedFindingBudgetBoundsTheMismatch(int budget, CheckStatus expected)
    {
        var fixture = await Fixture("mismatch");
        var result = await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, budget, default);
        Assert.Equal(expected, result!.Status);
        Assert.Equal(budget, result.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsPendingOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("mismatch");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FdaStudyIdMatchRule.InspectAsync(fixture.Packages,
            fixture.Lifecycle, fixture.Context, 20, cancellation.Token, result =>
            { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("match", CheckStatus.Pass)]
    [InlineData("mismatch", CheckStatus.Fail)]
    public async Task DeliveryAndReportContainTheSameQualifiedRuleResult(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        foreach (var input in fixture.Packages.Sequences.Values.Cast<Capture>()) input.Armed = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context)
            .InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.Files.Checks, check => check.RuleId == "FDA47-1738").Status);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, row => row.RuleId == "FDA47-1738").CheckStatus);
    }

    [Fact]
    public async Task LegacyCatalogDoesNotActivateTheRule()
    {
        var fixture = await Fixture("match");
        var original = fixture.Context;
        var catalog = new ValidationRuleCatalog(original.Catalog.Version, new string('a', 64), original.Catalog.Sources.Values,
            original.Catalog.Profiles.Values, original.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1738"));
        Assert.Null(await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle,
            new(catalog, original.Assets, original.NodeSchema), 20, default));
    }

    [Theory]
    [InlineData("A", CheckStatus.NotEvaluated)]
    [InlineData("B", CheckStatus.Fail)]
    public void OnlyPossibleMatchingRowCannotEstablishMatch(string possibleStudyId, CheckStatus expected)
    {
        // Deliberate consumer-layer facts: a possibly present row is not a definite match.
        var original = new XptRow(0, 1000, false, []);
        var capture = new CapturedXptFacts(CapturedXptStatus.Read, "source", new string('a', 64), "0001/ts.xpt", new string('b', 64), null);
        var summary = new PackageTrialSummaryFacts(TrialSummaryStatus.Read, capture, true, true, false, 0, 1,
            [new(0, 1000, false, possibleStudyId, "OTHER", "B", null, true, original)]);
        Assert.Equal(expected, FdaStudyIdMatchRule.Compare("A", summary, default));
    }

    [Fact]
    public async Task TwoStudiesSharingTsAreComparedIndependently()
    {
        var fixture = await Fixture("two-studies");
        var result = await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default);
        Assert.Equal(CheckStatus.Fail, result!.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("B", finding.Expected);
        Assert.Equal("0001/m4/study/stf-b.xml", finding.Location.LogicalPath);
    }

    [Fact]
    public async Task MultipleTsMismatchesShareBudgetAndRetainImmutableProgress()
    {
        var fixture = await Fixture("two-mismatches");
        var snapshots = new List<RuleCheckResult>();
        var result = await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 2, default,
            check => { if (check.Status == CheckStatus.Fail) snapshots.Add(check); });
        Assert.Equal(CheckStatus.Fail, result!.Status);
        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(2, snapshots.Count);
        Assert.Single(snapshots[0].Findings);
        Assert.Equal(2, snapshots[1].Findings.Count);
        var limited = await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 1, default);
        Assert.Single(limited!.Findings);
    }

    [Fact]
    public async Task KnownOutOfScopeOwnerIsNotPollutedByDocumentQualification()
    {
        var fixture = await Fixture("match", section: "m3.3");
        // Consumer-layer fact test: discovery/owner scope is known, while the selected STF
        // structure is unqualified. This does not claim a damaged XML parser run kept inventory complete.
        var inspections = fixture.Lifecycle.XmlInspections.Select(xml => new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId,
            xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones, xml.Checks, xml.RulesDigest,
            new PackageStfInspection(true, xml.StudyTaggingFiles.Documents.Select(stf => new ParsedStudyTaggingFile(stf.LogicalPath,
                stf.CapturedSha256, stf.ReadComplete, CheckStatus.Fail, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets)))));
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, inspections,
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var data = await PackageStudyDataInspector.InspectAsync(fixture.Packages, lifecycle, fixture.Context);
        Assert.True(data.ScopeComplete);
        Assert.False(Assert.Single(data.Documents).Stf.StructureComplete);
        Assert.All(data.Documents[0].Stf.Owners, owner => Assert.True(owner.Qualified));
        var result = await FdaStudyIdMatchRule.InspectAsync(fixture.Packages, lifecycle, fixture.Context, 20, default);
        Assert.Equal(CheckStatus.NotApplicable, result!.Status);
    }

    private static async Task<(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context)> Fixture(
        string scenario, string? date = "2026-11-20", string section = "m4.2.3.1")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario is "missing-source" or "missing-guide")
        {
            var hash = scenario == "missing-source" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" :
                "3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var target = new Capture(scenario, section, context);
        var history = scenario is "historical-match" or "historical-mismatch" or "reuse-history" ?
            new Capture(scenario == "historical-mismatch" ? "mismatch" : "match", section, context, target.Manifest.ApplicationId, "0000") : null;
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]),
            history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        Assert.All(lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), backbone => Assert.Equal(CheckStatus.Pass, backbone.DtdStatus));
        Assert.All(lifecycle.XmlInspections.SelectMany(xml => xml.StudyTaggingFiles.Documents), stf =>
            Assert.Equal(scenario == "duplicate-id" ? CheckStatus.Fail : CheckStatus.Pass, stf.DtdStatus));
        foreach (var input in packages.Sequences.Values.Cast<Capture>()) input.Armed = true;
        return (packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _scenario;
        public Capture(string scenario, string section, PackageStandardsContext context, Guid? appId = null, string sequence = "0001")
        {
            _scenario = scenario;
            var app = appId ?? Guid.NewGuid();
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var input = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")));
            var chain = new List<RATools.Domain.Ctd.SectionDefinition>();
            for (var node = context.NodeSchema.Definitions.Values.Single(item => item.SectionPath == section); ; node = context.NodeSchema.Get(node.ParentDefinitionKey!))
            { chain.Add(node); if (node.ParentDefinitionKey is null) break; }
            var parent = root;
            foreach (var node in chain.AsEnumerable().Reverse())
            {
                var child = new XElement(node.ElementName);
                foreach (var attribute in node.Attributes.Where(attribute => attribute.Required))
                    child.SetAttributeValue(attribute.Name, attribute.AllowedValues.Count == 0 ? "Valid" : attribute.AllowedValues[0]);
                parent.Add(child); parent = child;
            }
            parent.Add(Leaf("stf", "m4/study/stf.xml"));
            if (scenario == "two-studies") parent.Add(Leaf("stf-b", "m4/study/stf-b.xml"));
            var references = new List<string>();
            if (scenario != "no-ts")
            {
                if (scenario is "historical-match" or "historical-mismatch" or "history-absent") references.Add(Doc("../../../0000/index.xml#ts"));
                else
                {
                    parent.Add(Leaf("ts", scenario == "reuse-history" ? "../0000/m4/study/ts.xpt" : "m4/study/ts.xpt"));
                    references.Add(Doc("../../index.xml#" + (scenario == "missing-leaf" ? "absent" : "ts")));
                    if (scenario is not ("missing-file" or "reuse-history")) _files["0001/m4/study/ts.xpt"] = Dataset(scenario);
                }
            }
            if (scenario is "multiple-ts" or "two-mismatches")
            {
                parent.Add(Leaf("second", "m4/study/second/ts.xpt"));
                references.Add(Doc("../../index.xml#second"));
                _files["0001/m4/study/second/ts.xpt"] = Dataset("mismatch");
            }
            if (scenario is "unrelated-missing" or "known-fail-unknown")
            {
                parent.Add(Leaf("unrelated", "m4/study/dm.xpt"));
                references.Add(Doc("../../index.xml#" + (scenario == "known-fail-unknown" ? "unresolved" : "unrelated")));
            }
            if (scenario == "duplicate-ts-reference") references.Add(Doc("../../index.xml#ts"));
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            _files["0001/m4/study/stf.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
                "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>Study</title>" +
                "<study-id>" + (scenario switch { "blank-id" => "", "whitespace-id" => "   ", "stf-space" => "A ", "encoded-literal" => "A&amp;B", "no-second-decode" => "A&amp;amp;B", _ => "A" }) + "</study-id>" + (scenario == "duplicate-id" ? "<study-id>B</study-id>" : "") +
                "</study-identifier><study-document>" + string.Join("", references) + "</study-document></ectd:study>");
            if (scenario == "two-studies") _files["0001/m4/study/stf-b.xml"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(_files["0001/m4/study/stf.xml"])
                .Replace("<study-id>A</study-id>", "<study-id>B</study-id>", StringComparison.Ordinal));
            if (sequence != "0001") _files = _files.ToDictionary(file => sequence + file.Key[4..], file => file.Value, StringComparer.Ordinal);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static byte[] Dataset(string scenario)
        {
            if (scenario == "unsupported") return Encoding.ASCII.GetBytes(new string('x', 80));
            var columns = new[] { new PackageXptReaderTests.Column("STUDYID", 2, scenario.StartsWith("tail-", StringComparison.Ordinal) ? 8 : 24),
                new("TSPARMCD", 2, 8), new("TSVAL", 2, scenario.StartsWith("tail-", StringComparison.Ordinal) ? 16 : 40), new("TSVALNF", 2, 8) };
            var values = scenario switch
            {
                "sprefid" => new[] { "B", "SPREFID", "A", "" },
                "mismatch" or "known-fail-unknown" or "tail-mismatch" or "multiple-mismatch" or "duplicate-ts-reference" or "two-mismatches" => ["B", "SPREFID", "C", ""],
                "encoded-literal" or "no-second-decode" => ["A&B", "OTHER", "B", ""],
                "case" => ["a", "OTHER", "B", ""],
                "leading-space" => [" A", "OTHER", "B", ""],
                "lower-parameter" => ["B", "sprefid", "A", ""],
                "matching-id-unknown-value" => ["A", "SPREFID", "研究", ""],
                "matching-value-unknown-id" => ["研究", "SPREFID", "A", ""],
                "unknown-optional" => ["A", "OTHER", "B", "研究"],
                "unknown-irrelevant-value" => ["B", "OTHER", "研究", ""],
                "unknown-id" => ["研究", "OTHER", "B", ""],
                "unknown-parameter" => ["B", "研究", "A", ""],
                "unknown-parameter-nonmatching-value" => ["B", "研究", "C", ""],
                "unknown-sprefid" => ["B", "SPREFID", "研究", ""],
                _ => ["A", "OTHER", "B", ""]
            };
            var rows = new List<byte[]> { PackageXptReaderTests.TextRow(columns, values) };
            if (scenario is "multiple-match" or "multiple-mismatch") rows.Insert(0, PackageXptReaderTests.TextRow(columns, "D", "SPREFID", "E", ""));
            var bytes = PackageXptReaderTests.Sample(columns, rows.ToArray());
            if (scenario == "wrong-member") Encoding.ASCII.GetBytes("DM      ").CopyTo(bytes, 408);
            return bytes;
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        private static string Doc(string href) => "<doc-content xlink:href='" + href + "'><file-tag name='data-tabulation-dataset-send' info-type='ich'/></doc-content>";
        public bool Armed { get; set; }
        public string SourceId => "fda1738-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path)
        {
            if (Armed && path.EndsWith(".xml", StringComparison.Ordinal)) throw new InvalidOperationException("Use frozen XML facts.");
            if (Armed && _scenario == "ts-io" && path.EndsWith("/ts.xpt", StringComparison.Ordinal)) throw new IOException("Simulated read failure.");
            return new MemoryStream(_files[path], false);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
