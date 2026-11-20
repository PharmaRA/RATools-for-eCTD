using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaDuplicateStudyDatasetRuleTests
{
    [Theory]
    [InlineData("duplicate", CheckStatus.Fail)]
    [InlineData("same-file", CheckStatus.Fail)]
    [InlineData("different-bytes", CheckStatus.Fail)]
    [InlineData("encoded-name", CheckStatus.Fail)]
    [InlineData("duplicate-reference", CheckStatus.Pass)]
    [InlineData("different-name", CheckStatus.Pass)]
    [InlineData("different-title", CheckStatus.Pass)]
    [InlineData("title-space", CheckStatus.Pass)]
    [InlineData("name-case", CheckStatus.Pass)]
    [InlineData("title-case", CheckStatus.Pass)]
    [InlineData("replace", CheckStatus.Pass)]
    [InlineData("append", CheckStatus.Pass)]
    [InlineData("not-xpt", CheckStatus.NotEvaluated)]
    [InlineData("missing-file", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-plus-missing", CheckStatus.Fail)]
    [InlineData("separate-stfs", CheckStatus.NotEvaluated)]
    [InlineData("two-owners", CheckStatus.Fail)]
    [InlineData("empty", CheckStatus.NotApplicable)]
    public async Task ActualLeafEventsAndExactPairDetermineKnownDuplicates(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var check = await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default);
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        if (expected != CheckStatus.Fail) Assert.Empty(check.Findings);
        else
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("second", finding.Location.LeafId);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Contains("first", finding.Actual, StringComparison.Ordinal);
            Assert.Contains("second", finding.Actual, StringComparison.Ordinal);
            Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0001/m4/study/stf.xml");
        }
    }

    [Theory]
    [InlineData("m4.3", CheckStatus.Fail)]
    [InlineData("m5.2", CheckStatus.Fail)]
    [InlineData("m5.3.6", CheckStatus.Fail)]
    [InlineData("m5.4", CheckStatus.Fail)]
    [InlineData("m3.3", CheckStatus.NotApplicable)]
    public async Task ActualStfsAreNotExemptMerelyBecauseTheyAreOptional(string section, CheckStatus expected)
    {
        var fixture = await Fixture("duplicate", section: section);
        Assert.Equal(expected, (await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default))!.Status);
    }

    [Theory]
    [InlineData("duplicate", "2023-03-15", CheckStatus.NotApplicable)]
    [InlineData("duplicate", "2023-03-16", CheckStatus.Fail)]
    [InlineData("duplicate", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-guide", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("missing-criteria", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task FrozenSourcesAndDateGateTheRule(string scenario, string? date, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, date);
        Assert.Equal(expected, (await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default))!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingsRespectSharedBudget(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("duplicate");
        var check = await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, default);
        Assert.Equal(expected, check!.Status);
        Assert.Equal(remaining, check.Findings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgressRetainsIncompleteOrKnownFailureBeforeCancellation(bool afterFailure)
    {
        var fixture = await Fixture("duplicate");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages,
            fixture.Lifecycle, fixture.Context, 20, cancellation.Token, check =>
            { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("duplicate", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("different-title", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("all-nonnew", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    public async Task ReportDoesNotPromotePartialPass(string scenario, CheckStatus component, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario);
        ((Capture)fixture.Packages.Target).Armed = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context)
            .InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(component, Assert.Single(delivery.Files.Checks, item => item.RuleId == "FDA47-1737").Status);
        Assert.Equal(coverage, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, row => row.RuleId == "FDA47-1737").CheckStatus);
    }

    [Fact]
    public async Task LegacyCatalogDoesNotActivateTheNewRule()
    {
        var fixture = await Fixture("duplicate");
        var original = fixture.Context;
        var catalog = new ValidationRuleCatalog(original.Catalog.Version, new string('a', 64), original.Catalog.Sources.Values,
            original.Catalog.Profiles.Values, original.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1737"));
        Assert.Null(await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle,
            new(catalog, original.Assets, original.NodeSchema), 20, default));
    }

    [Theory]
    [InlineData("historical-content", 2)]
    [InlineData("historical-first", 1)]
    [InlineData("historical-second", 1)]
    public async Task CurrentNewLeavesReusingHistoricalFilesKeepTheirRealCaptureLocations(string scenario, int expectedHistorical)
    {
        var fixture = await Fixture(scenario);
        var check = await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default);
        Assert.Equal(CheckStatus.Fail, check!.Status);
        var finding = Assert.Single(check.Findings);
        var historicalFiles = finding.Location.Files!.Where(file => file.LogicalPath.StartsWith("0000/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expectedHistorical, historicalFiles.Length);
        Assert.All(historicalFiles, file => Assert.Equal(fixture.Packages.Sequences["0000"].SourceId, file.SourceId));
        Assert.All(finding.Location.Files!.Where(file => file.LogicalPath.StartsWith("0001/", StringComparison.Ordinal)),
            file => Assert.Equal(fixture.Packages.Target.SourceId, file.SourceId));
        Assert.Contains(finding.Location.Files!, file => file.LogicalPath == "0001/index.xml" && file.SourceId == fixture.Packages.Target.SourceId);
    }

    [Fact]
    public async Task RepeatedHistoricalLeafReferenceIsNotTwoCurrentNewEvents()
    {
        var fixture = await Fixture("historical-reference");
        var check = await FdaDuplicateStudyDatasetRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default);
        Assert.Equal(CheckStatus.NotEvaluated, check!.Status);
        Assert.Empty(check.Findings);
    }

    private static async Task<(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context)> Fixture(
        string scenario, string? date = "2026-11-20", string section = "m4.2.3.1")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario is "missing-guide" or "missing-criteria")
        {
            var hash = scenario == "missing-guide" ? "3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689" :
                "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var target = new Capture(scenario, section, context);
        var historical = scenario is "historical-content" or "historical-first" or "historical-second" or "historical-reference" ?
            new Capture("duplicate", section, context, target.Manifest.ApplicationId, "0000") : null;
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", historical is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, historical.SourceId, historical.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]),
            historical is null ? [] : [historical]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        Assert.All(lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), backbone => Assert.Equal(CheckStatus.Pass, backbone.DtdStatus));
        Assert.All(lifecycle.XmlInspections.SelectMany(xml => xml.StudyTaggingFiles.Documents), stf => Assert.Equal(CheckStatus.Pass, stf.DtdStatus));
        foreach (var capture in packages.Sequences.Values.Cast<Capture>()) capture.Armed = true;
        return (packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string scenario, string section, PackageStandardsContext context, Guid? applicationId = null, string sequence = "0001")
        {
            var app = applicationId ?? Guid.NewGuid();
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")));
            var definition = context.NodeSchema.Definitions.Values.Single(item => item.SectionPath == section);
            var chain = new List<RATools.Domain.Ctd.SectionDefinition>();
            for (var item = definition; ; item = context.NodeSchema.Get(item.ParentDefinitionKey!))
            { chain.Add(item); if (item.ParentDefinitionKey is null) break; }
            var parent = root;
            foreach (var item in chain.AsEnumerable().Reverse())
            {
                var node = new XElement(item.ElementName);
                foreach (var attribute in item.Attributes.Where(attribute => attribute.Required))
                    node.SetAttributeValue(attribute.Name, attribute.AllowedValues.Count == 0 ? "Valid" : attribute.AllowedValues[0]);
                parent.Add(node); parent = node;
            }
            parent.Add(Leaf("stf", "m4/study/stf.xml"));
            if (scenario == "two-owners") parent.Add(Leaf("other-owner", "m4/study/stf.xml"));
            var secondName = scenario == "different-name" ? "ae.xpt" : scenario == "name-case" ? "DM.xpt" : "dm.xpt";
            var secondPath = scenario == "same-file" ? "m4/study/one/dm.xpt" : "m4/study/two/" + secondName;
            var secondHref = scenario == "encoded-name" ? secondPath.Replace("dm.xpt", "%64m.xpt", StringComparison.Ordinal) : secondPath;
            var secondTitle = scenario == "different-title" ? "Other" : scenario == "title-case" ? "DATA" : scenario == "title-space" ? " Data" : "Data";
            var operation = scenario == "all-nonnew" ? "append" : scenario is "replace" or "append" ? scenario : "new";
            var references = new List<string>();
            if (scenario != "empty")
            {
                parent.Add(Leaf("first", scenario is "historical-content" or "historical-first" ? "../0000/m4/study/one/dm.xpt" : "m4/study/one/dm.xpt",
                    "Data", scenario == "all-nonnew" ? "replace" : "new"));
                references.Add(Doc("first"));
                if (scenario == "duplicate-reference") references.Add(Doc("first"));
                else
                {
                    parent.Add(Leaf("second", scenario is "historical-content" or "historical-second" ? "../0000/" + secondHref : secondHref, secondTitle, operation));
                    if (scenario != "separate-stfs") references.Add(Doc("second"));
                }
            }
            if (scenario == "duplicate-plus-missing") references.Add(Doc("unknown"));
            if (scenario == "historical-reference")
            {
                references.Clear();
                var historicalReference = Doc("first").Replace("../../index.xml#first", "../../../0000/index.xml#first", StringComparison.Ordinal);
                references.Add(historicalReference); references.Add(historicalReference);
            }
            if (scenario == "separate-stfs")
            {
                parent.Add(Leaf("stf-b", "m4/study/stf-b.xml"));
                _files["0001/m4/study/stf-b.xml"] = Stf([Doc("second")]);
            }
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes(new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root).ToString());
            _files["0001/m4/study/stf.xml"] = Stf(references);
            var dataset = PackageXptReaderTests.Sample([new("STUDYID", 2, 80)], [Encoding.ASCII.GetBytes("A".PadRight(80))]);
            Encoding.ASCII.GetBytes("DM      ").CopyTo(dataset, 408);
            _files["0001/m4/study/one/dm.xpt"] = dataset;
            if (scenario != "missing-file")
            {
                var second = dataset.ToArray();
                if (scenario == "different-bytes") second[^80] = (byte)'B';
                _files["0001/" + secondPath] = scenario == "not-xpt" ? Encoding.ASCII.GetBytes(new string('x', 80)) : second;
            }
            if (sequence != "0001") _files = _files.ToDictionary(file => sequence + file.Key[4..], file => file.Value, StringComparer.Ordinal);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Leaf(string id, string href, string title = "Study", string operation = "new")
        {
            var leaf = new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", operation), new XAttribute("checksum", new string('0', 32)),
                new XAttribute("checksum-type", "md5"), new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", title));
            if (operation != "new") leaf.SetAttributeValue("modified-file", "../0000/index.xml#old");
            return leaf;
        }
        private static string Doc(string id) => "<doc-content xlink:href='../../index.xml#" + id + "'><file-tag name='data-tabulation-dataset-send' info-type='ich'/></doc-content>";
        private static byte[] Stf(IEnumerable<string> references) => Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
            "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>Study</title><study-id>A</study-id></study-identifier>" +
            "<study-document>" + string.Join("", references) + "</study-document></ectd:study>");
        public bool Armed { get; set; }
        public string SourceId => "fda1737-capture-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => Armed && path.EndsWith(".xml", StringComparison.Ordinal) ?
            throw new InvalidOperationException("Use frozen XML facts.") : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
