using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaTrialSummaryRequirementRuleTests
{
    [Theory]
    [InlineData("missing", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("missing", "m5.3.1.1", CheckStatus.Fail)]
    [InlineData("valid", "m4.2.3.1", CheckStatus.Pass)]
    [InlineData("valid", "m5.3.1.1", CheckStatus.Pass)]
    [InlineData("full-ts", "m4.2.3.1", CheckStatus.Pass)]
    [InlineData("no-ssd", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("empty-ssd", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("invalid-date", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("na", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("unread-ts", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("wrong-member", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("wrong-name", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("no-trigger", "m4.2.3.1", CheckStatus.NotApplicable)]
    [InlineData("report-only", "m5.3.1.1", CheckStatus.NotApplicable)]
    [InlineData("missing", "m4.3", CheckStatus.NotApplicable)]
    [InlineData("missing", "m5.3.6", CheckStatus.NotApplicable)]
    [InlineData("ind", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("other-app", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("anda", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("bla", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("xpt-only", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("report-missing", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("fake-trigger", "m5.3.1.1", CheckStatus.NotEvaluated)]
    [InlineData("ts-io", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("owner-ind", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("owner-nda", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("two-owners", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("missing-nonts", "m4.2.3.1", CheckStatus.Fail)]
    [InlineData("outside-unread", "m4.3", CheckStatus.NotApplicable)]
    [InlineData("outside-study", "m4.2.3.1", CheckStatus.Pass)]
    [InlineData("blank-tail", "m4.2.3.1", CheckStatus.Pass)]
    [InlineData("historical-ts", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("history-absent", "m4.2.3.1", CheckStatus.NotEvaluated)]
    [InlineData("reuse-ts", "m4.2.3.1", CheckStatus.Pass)]
    public async Task KnownTriggerMissingEvidenceAndRuntimeUnknownStayDistinct(string scenario, string section, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, section);
        var result = await FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal("0001/m4/study/stf.xml", finding.Location.LogicalPath);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("m4.2.3.2")]
    [InlineData("m4.2.3.4.1")]
    [InlineData("m5.3.1.2")]
    [InlineData("m5.3.3.1")]
    [InlineData("m5.3.3.2")]
    [InlineData("m5.3.3.3")]
    [InlineData("m5.3.3.4")]
    [InlineData("m5.3.4.1")]
    [InlineData("m5.3.5.1")]
    [InlineData("m5.3.5.2")]
    public async Task ExactOfficialSectionsAndTheirDescendantsAreChecked(string section)
    {
        var fixture = await Fixture("missing", section);
        Assert.Equal(CheckStatus.Fail, (await FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default))!.Status);
    }

    [Theory]
    [InlineData("missing", "m4.2.3.1", "2021-09-14", CheckStatus.NotApplicable)]
    [InlineData("missing", "m4.2.3.1", "2021-09-15", CheckStatus.NotEvaluated)]
    [InlineData("missing", "m4.2.3.1", "2023-03-15", CheckStatus.NotEvaluated)]
    [InlineData("missing", "m4.2.3.1", "2023-03-16", CheckStatus.Fail)]
    [InlineData("missing", "m5.3.1.1", "2021-09-15", CheckStatus.Fail)]
    [InlineData("missing", "m4.2.3.1", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-guide", "m4.2.3.1", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task DatesAndFrozenGuideGateTheKnownBranches(string scenario, string section, string? date, CheckStatus expected)
    {
        var fixture = await Fixture(scenario, section, date);
        Assert.Equal(expected, (await FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 20, default))!.Status);
    }

    [Fact]
    public async Task BudgetAndExplicitCancellationDoNotInventACompletedCheck()
    {
        var fixture = await Fixture("missing", "m4.2.3.1");
        Assert.Equal(CheckStatus.NotEvaluated, (await FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, 0, default))!.Status);
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages,
            fixture.Lifecycle, fixture.Context, 20, cancellation.Token, result => { retained = result; cancellation.Cancel(); }));
        Assert.Equal(CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("missing", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("valid", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    public async Task ReportRetainsFailuresButDoesNotPromotePartialPass(string scenario, CheckStatus component, CheckStatus coverage)
    {
        var fixture = await Fixture(scenario, "m4.2.3.1");
        ((Capture)fixture.Packages.Target).Armed = false;
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context)
            .InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(component, Assert.Single(delivery.Files.Checks, check => check.RuleId == "FDA47-1734").Status);
        Assert.Equal(coverage, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, row => row.RuleId == "FDA47-1734").CheckStatus);
    }

    [Fact]
    public async Task LegacyCatalogDoesNotBorrowCurrentRule()
    {
        var fixture = await Fixture("missing", "m4.2.3.1");
        var original = fixture.Context;
        var catalog = new ValidationRuleCatalog(original.Catalog.Version, new string('a', 64), original.Catalog.Sources.Values,
            original.Catalog.Profiles.Values, original.Catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1734"));
        Assert.Null(await FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages, fixture.Lifecycle,
            new(catalog, original.Assets, original.NodeSchema), 20, default));
    }

    [Fact]
    public async Task KnownFailureSurvivesCancellationInImmutableProgress()
    {
        var fixture = await Fixture("missing", "m4.2.3.1");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages,
            fixture.Lifecycle, fixture.Context, 20, cancellation.Token, result =>
            {
                retained = result;
                if (result.Status == CheckStatus.Fail) cancellation.Cancel();
            }));
        Assert.Equal(CheckStatus.Fail, retained!.Status);
        Assert.Single(retained.Findings);
    }

    internal static async Task<(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context)> Fixture(
        string scenario, string section, string? date = "2026-11-20")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (scenario == "missing-guide")
        {
            const string hash = "3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689";
            snapshot = RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != hash).ToArray() },
                snapshot.Resources.Where(resource => resource.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var target = new Capture(scenario, section, context);
        var historical = scenario is "historical-ts" or "reuse-ts" ? new Capture("valid", section, context, target.Manifest.ApplicationId, "0000") : null;
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", historical is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, historical.SourceId, historical.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]),
            historical is null ? [] : [historical]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            Assert.All(xml.Backbones, backbone => Assert.Equal(CheckStatus.Pass, backbone.DtdStatus));
            Assert.NotEmpty(xml.StudyTaggingFiles.Documents);
            Assert.All(xml.StudyTaggingFiles.Documents, stf => Assert.Equal(CheckStatus.Pass, stf.DtdStatus));
        }
        foreach (var input in packages.Sequences.Values.Cast<Capture>()) input.Armed = true;
        return (packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _scenario;
        public Capture(string scenario, string section, PackageStandardsContext context, Guid? applicationId = null, string sequence = "0001")
        {
            _scenario = scenario;
            var app = applicationId ?? Guid.NewGuid();
            var type = scenario is "ind" or "owner-ind" ? "fdaat4" : scenario == "other-app" ? "fdaat5" :
                scenario == "anda" ? "fdaat2" : scenario == "bla" ? "fdaat3" : "fdaat1";
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid()
                .Replace("application-type=\"fdaat4\"", "application-type=\"" + type + "\"", StringComparison.Ordinal));
            regional.Descendants("sequence-number").Single().Value = sequence;
            if (scenario is "owner-ind" or "owner-nda" or "two-owners")
            {
                var application = regional.Descendants("application").Single();
                var other = new XElement(application);
                other.SetAttributeValue("application-containing-files", scenario == "two-owners" ? "true" : "false");
                other.Descendants("application-number").Single().SetAttributeValue("application-type", scenario == "owner-ind" ? "fdaat1" : "fdaat4");
                other.Descendants("application-number").Single().Value = "654321";
                application.AddBeforeSelf(other);
            }
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var input = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var root = new XElement(XName.Get("ectd", "http://www.ich.org/ectd"), new XAttribute(XNamespace.Xmlns + "ectd", "http://www.ich.org/ectd"),
                new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3c.org/1999/xlink"), new XAttribute("dtd-version", "3.2"),
                new XElement("m1-administrative-information-and-prescribing-information", Leaf("regional", "m1/us/us-regional.xml")));
            var definition = context.NodeSchema.Definitions.Values.Single(item => item.SectionPath == section);
            var chain = new List<RATools.Domain.Ctd.SectionDefinition>();
            for (var current = definition; ; current = context.NodeSchema.Get(current.ParentDefinitionKey!))
            { chain.Add(current); if (current.ParentDefinitionKey is null) break; }
            var parent = root;
            foreach (var item in chain.AsEnumerable().Reverse())
            {
                var node = new XElement(item.ElementName);
                foreach (var attribute in item.Attributes.Where(attribute => attribute.Required))
                    node.SetAttributeValue(attribute.Name, attribute.AllowedValues.Count == 0 ? "Valid" : attribute.AllowedValues[0]);
                parent.Add(node); parent = node;
            }
            parent.Add(Leaf("stf", "m4/study/stf.xml"));
            var references = new List<string>();
            var clinical = section.StartsWith("m5", StringComparison.Ordinal);
            if (scenario != "no-trigger")
            {
                var reportOnly = !clinical && scenario != "xpt-only" || scenario == "report-only";
                parent.Add(Leaf("trigger", reportOnly ? "m4/study/report.pdf" : "m4/study/dm.xpt"));
                references.Add(Doc("trigger", reportOnly ? "study-report-body" : "data-tabulation-dataset-sdtm"));
                if (scenario != "report-missing")
                    _files[reportOnly ? "0001/m4/study/report.pdf" : "0001/m4/study/dm.xpt"] = reportOnly ? "%PDF-1.4 fixture"u8.ToArray() :
                        scenario == "fake-trigger" ? Encoding.ASCII.GetBytes(new string('x', 80)) : Dataset("A", "OTHER", "value", "", member: "DM");
            }
            if (scenario is not ("missing" or "no-trigger" or "report-only" or "ind" or "other-app" or "missing-guide" or
                "anda" or "bla" or "xpt-only" or "report-missing" or "fake-trigger" or "owner-ind" or "owner-nda" or "two-owners" or "missing-nonts" or "historical-ts" or "history-absent"))
            {
                var filename = scenario == "wrong-name" ? "other.xpt" : "ts.xpt";
                parent.Add(Leaf("ts", scenario == "reuse-ts" ? "../0000/m4/study/ts.xpt" : "m4/study/" + filename));
                references.Add(Doc("ts", "data-tabulation-dataset-send"));
                var parameter = scenario == "no-ssd" ? "SPREFID" : clinical ? "SSTDTC" : "STSTDTC";
                var value = scenario is "empty-ssd" or "na" ? "" : scenario == "invalid-date" ? "2020-02-31" : "2020-01-01";
                if (scenario != "reuse-ts")
                    _files["0001/m4/study/" + filename] = scenario is "unread-ts" or "outside-unread" ? Encoding.ASCII.GetBytes(new string('x', 80)) :
                        Dataset("A", parameter, value, scenario == "na" ? "NA" : "", scenario == "full-ts", scenario == "wrong-member" ? "DM" : "TS", scenario == "blank-tail");
            }
            if (scenario is "historical-ts" or "history-absent")
                references.Add(Doc("ts", "data-tabulation-dataset-send").Replace("../../index.xml#ts", "../../../0000/index.xml#ts", StringComparison.Ordinal));
            if (scenario == "missing-nonts")
            {
                parent.Add(Leaf("missing-dm", "m4/study/dm.xpt"));
                references.Add(Doc("missing-dm", "data-tabulation-dataset-send"));
            }
            if (scenario == "outside-study")
            {
                root.Element("m4-nonclinical-study-reports")!.Add(new XElement("m4-3-literature-references",
                    Leaf("outside-stf", "m4/study/outside.xml"), Leaf("outside-data", "m4/study/absent.xpt")));
                _files["0001/m4/study/outside.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
                    "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>Other</title><study-id>B</study-id></study-identifier>" +
                    "<study-document>" + Doc("outside-data", "data-tabulation-dataset-send") + "</study-document></ectd:study>");
            }
            var index = new XDocument(new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null), root);
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes(index.ToString());
            _files["0001/m4/study/stf.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
                "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>Study</title><study-id>A</study-id></study-identifier>" +
                "<study-document>" + string.Join("", references) + "</study-document></ectd:study>");
            if (sequence != "0001") _files = _files.ToDictionary(file => sequence + file.Key[4..], file => file.Value, StringComparer.Ordinal);
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static byte[] Dataset(string study, string parameter, string value, string nullFlavor, bool full = false, string member = "TS", bool shortRow = false)
        {
            var columns = new[] { new PackageXptReaderTests.Column("STUDYID", 2, shortRow ? 8 : 24), new("TSPARMCD", 2, 8), new("TSVAL", 2, shortRow ? 16 : 40), new("TSVALNF", 2, 8) };
            var rows = new List<byte[]> { PackageXptReaderTests.TextRow(columns, study, parameter, value, nullFlavor) };
            if (full) rows.Insert(0, PackageXptReaderTests.TextRow(columns, study, "SPREFID", "A", ""));
            var bytes = PackageXptReaderTests.Sample(columns, rows.ToArray());
            Encoding.ASCII.GetBytes(member.PadRight(8)).CopyTo(bytes, 408);
            return bytes;
        }
        private static XElement Leaf(string id, string href) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
            new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        private static string Doc(string id, string tag) => "<doc-content xlink:href='../../index.xml#" + id + "'><file-tag name='" + tag + "' info-type='ich'/></doc-content>";
        public bool Armed { get; set; }
        public string SourceId => "fda1734-memory-capture-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path)
        {
            if (Armed && path.EndsWith(".xml", StringComparison.Ordinal)) throw new InvalidOperationException("The rule must consume frozen XML observations.");
            if (Armed && _scenario == "ts-io" && path.EndsWith("/ts.xpt", StringComparison.Ordinal)) throw new IOException("Simulated TS read failure.");
            return new MemoryStream(_files[path], false);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
