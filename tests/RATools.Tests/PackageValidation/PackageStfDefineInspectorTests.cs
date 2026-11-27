using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageStfDefineInspectorTests
{
    [Theory]
    [InlineData("valid", true, "Equal", "Equal")]
    [InlineData("different-name", true, "Different", "Equal")]
    [InlineData("different-sas-name", true, "Equal", "Different")]
    [InlineData("duplicate-oid", false, "Equal", "Equal")]
    public async Task DatasetIdentityIsComparedAgainstTheActualMember(string scenario, bool unique, string name, string sasName)
    {
        var result = await Inspect(await Fixture(scenario));
        Assert.Equal(StfDefineInspectionStatus.Read, result.Status);
        Assert.NotEmpty(result.Datasets);
        foreach (var dataset in result.Datasets)
        {
            Assert.Equal("DM", dataset.Content.Xpt!.Content!.MemberName);
            Assert.Equal(scenario == "different-name" ? "OTHER" : "DM", dataset.Content.Declaration.Name);
            Assert.Equal(scenario == "different-sas-name" ? "OTHER" : "DM", dataset.Content.Declaration.SasDatasetName);
            Assert.Equal(unique, dataset.Identity.UniqueDeclarationOid);
            Assert.Equal(name, dataset.Identity.NameToMember.ToString());
            Assert.Equal(sasName, dataset.Identity.SasNameToMember.ToString());
        }
        Assert.Equal(scenario == "duplicate-oid" ? 2 : 1, result.Datasets.Count);
    }

    [Theory]
    [InlineData("missing-name", true, "Unknown", "Equal")]
    [InlineData("empty-name", true, "Unknown", "Equal")]
    [InlineData("missing-sas-name", true, "Equal", "Unknown")]
    [InlineData("empty-sas-name", true, "Equal", "Unknown")]
    [InlineData("lower-name", true, "Different", "Equal")]
    [InlineData("space-name", true, "Different", "Equal")]
    [InlineData("lower-sas-name", true, "Equal", "Different")]
    [InlineData("space-sas-name", true, "Equal", "Different")]
    [InlineData("missing-oid", false, "Equal", "Equal")]
    [InlineData("empty-oid", false, "Equal", "Equal")]
    [InlineData("different-member", true, "Different", "Different")]
    [InlineData("bad-data", true, "Unknown", "Unknown")]
    [InlineData("missing-data", true, "Unknown", "Unknown")]
    [InlineData("unreadable-data", true, "Unknown", "Unknown")]
    public async Task UnknownNamesAndLiteralDifferencesRemainIndependent(string scenario, bool unique, string name, string sasName)
    {
        var result = await Inspect(await Fixture(scenario));
        Assert.Equal(StfDefineInspectionStatus.Read, result.Status);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(StfDatasetReferenceStatus.Referenced, dataset.ReferenceStatus);
        Assert.Equal(unique, dataset.Identity.UniqueDeclarationOid);
        Assert.Equal(name, dataset.Identity.NameToMember.ToString());
        Assert.Equal(sasName, dataset.Identity.SasNameToMember.ToString());
        if (scenario == "different-member")
        {
            Assert.Equal("0001/m4/study/dm.xpt", dataset.Content.LogicalPath);
            Assert.Equal("OTHER", dataset.Content.Xpt!.Content!.MemberName);
        }
    }

    [Theory]
    [InlineData("valid", "Referenced")]
    [InlineData("unreferenced", "NotReferenced")]
    [InlineData("other-stf", "NotReferenced")]
    [InlineData("two-leaves", "MultipleLeaves")]
    [InlineData("repeated-reference", "Referenced")]
    [InlineData("unknown-reference", "Unknown")]
    [InlineData("known-with-unknown", "Referenced")]
    [InlineData("wrong-tag", "Referenced")]
    public async Task OnlyTheSelectedStfCanSupplyDatasetReferences(string scenario, string expected)
    {
        var f = await Fixture(scenario);
        var result = await Inspect(f);
        Assert.Equal(StfDefineInspectionStatus.Read, result.Status);
        Assert.Equal("A", result.StudyId);
        Assert.Equal(StfPath, result.Stf!.Document.LogicalPath);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(expected, dataset.ReferenceStatus.ToString());
        Assert.Equal("0001/m4/study/dm.xpt", dataset.Content.LogicalPath);
        Assert.Equal("DM", dataset.Content.Xpt!.Content!.MemberName);
        Assert.Equal("A", Assert.Single(dataset.StudyIds).Value);
        Assert.True(dataset.StudyIdsComplete);
        Assert.Equal(expected is "NotReferenced" or "Unknown" ? 0 : expected == "MultipleLeaves" || scenario == "repeated-reference" ? 2 : 1, dataset.References.Count);
        Assert.Equal("STUDY.A", result.Define!.Define!.Content!.StudyOid); // ODM OID is not STF study-id.
        Assert.Equal(2, f.Target.Opens.Count);
    }

    [Theory]
    [InlineData("different-study", "B", true)]
    [InlineData("missing-study-column", null, false)]
    [InlineData("undecoded-study", null, false)]
    [InlineData("possible-row", "A", false)]
    [InlineData("empty-study", "", true)]
    public async Task StudyIdentityIsObservedWithoutInventingAnEqualityPolicy(string scenario, string? value, bool complete)
    {
        var result = await Inspect(await Fixture(scenario));
        Assert.Equal(StfDefineInspectionStatus.Read, result.Status);
        Assert.Equal("A", result.StudyId);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(complete, dataset.StudyIdsComplete);
        if (scenario == "missing-study-column") Assert.Empty(dataset.StudyIds);
        else Assert.Equal(value, dataset.StudyIds[0].Value);
        if (scenario == "possible-row") Assert.Contains(dataset.StudyIds, row => !row.Certain);
        if (scenario == "undecoded-study") Assert.False(dataset.StudyIds[0].Decoded);
        if (scenario == "empty-study")
        {
            Assert.Equal(1, dataset.Content.Xpt!.Content!.MinPossibleRows);
            Assert.Equal(1, dataset.Content.Xpt.Content.MaxPossibleRows); // 80 bytes cannot all be legal 0..79-byte padding.
        }
    }

    [Theory]
    [InlineData("wrong-rules", "StfUnavailable")]
    [InlineData("wrong-assets", "StfUnavailable")]
    [InlineData("duplicate-xml", "StfUnavailable")]
    [InlineData("define-not-referenced", "DefineUnavailable")]
    [InlineData("missing-define", "StfUnavailable")]
    [InlineData("bad-define", "DefineUnavailable")]
    public async Task UnboundOrUnavailableInputsDoNotPublishAssociations(string scenario, string expected)
    {
        var f = await Fixture(scenario);
        if (scenario is "wrong-rules" or "wrong-assets" or "duplicate-xml")
        {
            var lifecycle = f.Lifecycle;
            f = f with { Lifecycle = new(f.Packages, lifecycle.ProfileSnapshotId,
                scenario == "duplicate-xml" ? lifecycle.XmlInspections.Concat(lifecycle.XmlInspections) : lifecycle.XmlInspections,
                lifecycle.Events, lifecycle.States, lifecycle.Checks,
                scenario == "wrong-rules" ? "different" : lifecycle.RulesDigest,
                scenario == "wrong-assets" ? "different" : lifecycle.AssetManifestDigest) };
        }
        var result = await Inspect(f);
        Assert.Equal(expected, result.Status.ToString());
        Assert.Null(result.Stf); Assert.Null(result.Define); Assert.Empty(result.Datasets);
        if (scenario is "wrong-rules" or "wrong-assets" or "duplicate-xml" or "define-not-referenced") Assert.Empty(f.Target.Opens);
    }

    [Theory]
    [InlineData("missing-data", "Missing")]
    [InlineData("bad-data", "Read")]
    [InlineData("unreadable-data", "Unavailable")]
    public async Task StfReferenceAndActualContentAvailabilityAreDifferentFacts(string scenario, string contentStatus)
    {
        var result = await Inspect(await Fixture(scenario));
        Assert.Equal(StfDefineInspectionStatus.Read, result.Status);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(StfDatasetReferenceStatus.Referenced, dataset.ReferenceStatus);
        Assert.Equal(contentStatus, dataset.Content.Status.ToString());
        Assert.False(dataset.StudyIdsComplete);
        Assert.Empty(dataset.StudyIds);
    }

    [Theory]
    [InlineData("hash", "InputChanged")]
    [InlineData("final-change", "InputChanged")]
    [InlineData("final-io", "Unavailable")]
    [InlineData("identity-source", "InputChanged")]
    public async Task FinalIntegrityFailureClearsEveryEarlierObservation(string scenario, string expected)
    {
        var f = await Fixture(scenario);
        var result = await Inspect(f);
        Assert.Equal(expected, result.Status.ToString());
        Assert.Empty(result.Datasets); Assert.Null(result.Define); Assert.Null(result.Stf); Assert.Null(result.StudyId);
        Assert.Equal(2, f.Target.Opens.Count);
    }

    [Theory]
    [InlineData("application")]
    [InlineData("sequence")]
    [InlineData("leaf")]
    [InlineData("stf")]
    public async Task ExplicitSelectionCannotBorrowAnotherIdentity(string field)
    {
        var f = await Fixture("valid");
        var result = await PackageStfDefineInspector.InspectAsync(f.Packages, f.Lifecycle, f.Context,
            field == "stf" ? "0001/m4/study/absent.xml" : StfPath,
            new(field == "application" ? Guid.NewGuid() : f.Target.Manifest.ApplicationId, field == "sequence" ? "0000" : "0001", "index.xml", field == "leaf" ? "absent" : "define"));
        Assert.NotEqual(StfDefineInspectionStatus.Read, result.Status);
        Assert.Empty(result.Datasets); Assert.Empty(f.Target.Opens);
    }

    [Fact]
    public async Task SharedReaderLimitsAndSelectionDigestAreRetained()
    {
        var f = await Fixture("valid");
        var original = await Inspect(f);
        var limited = await PackageStfDefineInspector.InspectAsync(f.Packages, f.Lifecycle, f.Context, StfPath,
            new(f.Target.Manifest.ApplicationId, "0001", "index.xml", "define"), xptLimits: new(MaxRows: 0));
        Assert.Equal(StfDefineInspectionStatus.Read, limited.Status);
        Assert.Equal(XptReadStatus.LimitExceeded, Assert.Single(limited.Datasets).Content.Xpt!.Content!.Status);
        Assert.Equal(DatasetNameComparison.Unknown, limited.Datasets[0].Identity.NameToMember);
        Assert.Equal(DatasetNameComparison.Unknown, limited.Datasets[0].Identity.SasNameToMember);
        Assert.NotEqual(original.SelectionDigest, limited.SelectionDigest);
        Assert.Throws<NotSupportedException>(() => ((IList<StfDefineDataset>)original.Datasets).Clear());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPropagatesBeforeAndDuringContentReading(bool during)
    {
        var f = await Fixture("valid");
        using var cancellation = new CancellationTokenSource();
        if (during) f.Target.CancelRead = cancellation.Cancel; else cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageStfDefineInspector.InspectAsync(f.Packages,
            f.Lifecycle, f.Context, StfPath, new(f.Target.Manifest.ApplicationId, "0001", "index.xml", "define"), cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("encoded-data")]
    [InlineData("query-data")]
    public async Task UnsupportedLeafPathsCannotSupplyAnExactReference(string scenario)
    {
        var f = await Fixture(scenario);
        var result = await Inspect(f);
        Assert.Equal(StfDefineInspectionStatus.Read, result.Status);
        Assert.Equal(StfDatasetReferenceStatus.Unknown, Assert.Single(result.Datasets).ReferenceStatus);
        Assert.Empty(result.Datasets[0].References);
        Assert.Equal(2, f.Target.Opens.Count); // define still independently names a readable XPT.
    }

    [Fact]
    public async Task ABindingBudgetInterruptionCannotPublishPartialFacts()
    {
        var f = await Fixture("valid");
        f.Target.SelectedLimits = new() { MaxXmlNodes = 1 };
        var original = f.Lifecycle;
        var xml = original.XmlInspections.Select(x => new PackageXmlInspection(x.InputDigest, x.ProfileSnapshotId,
            x.AssetManifestDigest, f.Target.Limits.Digest(), x.Backbones, x.Checks, x.RulesDigest, x.StudyTaggingFiles));
        f = f with { Lifecycle = new(f.Packages, original.ProfileSnapshotId, xml, original.Events, original.States, original.Checks,
            original.RulesDigest, original.AssetManifestDigest) };
        var result = await Inspect(f);
        Assert.Equal(StfDefineInspectionStatus.StfUnavailable, result.Status);
        Assert.Empty(result.Datasets); Assert.Null(result.Stf); Assert.Empty(f.Target.Opens);
    }

    [Fact]
    public async Task ExplicitUtf8ChangesTheObservationWithoutChangingTheStfIdentity()
    {
        var f = await Fixture("undecoded-study");
        var result = await PackageStfDefineInspector.InspectAsync(f.Packages, f.Lifecycle, f.Context, StfPath,
            new(f.Target.Manifest.ApplicationId, "0001", "index.xml", "define"), encoding: XptTextEncoding.Utf8);
        Assert.Equal("A", result.StudyId);
        var dataset = Assert.Single(result.Datasets);
        Assert.True(dataset.StudyIdsComplete);
        Assert.Equal("研究", Assert.Single(dataset.StudyIds).Value);
    }

    private const string StfPath = "0001/m4/study/stf.xml";
    private sealed record FixtureData(Capture Target, PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static Task<PackageStfDefineInspection> Inspect(FixtureData f) => PackageStfDefineInspector.InspectAsync(f.Packages,
        f.Lifecycle, f.Context, StfPath, new(f.Target.Manifest.ApplicationId, "0001", "index.xml", "define"));
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = PackageStandardsContext.Current;
        var target = new Capture(scenario);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var xml = Assert.Single(lifecycle.XmlInspections);
        Assert.All(xml.Backbones, b => Assert.Equal(CheckStatus.Pass, b.DtdStatus));
        Assert.All(xml.StudyTaggingFiles.Documents, s => Assert.Equal(CheckStatus.Pass, s.DtdStatus));
        Assert.NotEmpty(xml.StudyTaggingFiles.Documents);
        target.Armed = true;
        return new(target, packages, lifecycle, context);
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _scenario;
        private int _verifications;
        public Capture(string scenario)
        {
            _scenario = scenario;
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:ectd SYSTEM 'util/dtd/ich-ectd-3-2.dtd'>" +
                "<ectd:ectd xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3c.org/1999/xlink' dtd-version='3.2'>" +
                "<m1-administrative-information-and-prescribing-information>" + Leaf("regional", "m1/us/us-regional.xml") + "</m1-administrative-information-and-prescribing-information>" +
                "<m4-nonclinical-study-reports><m4-3-literature-references>" + Leaf("stf", "m4/study/stf.xml") +
                Leaf("define", "m4/study/define.xml") + Leaf("data", scenario == "encoded-data" ? "m4/study/%64m.xpt" :
                    scenario == "query-data" ? "m4/study/dm.xpt?x" : "m4/study/dm.xpt") +
                (scenario == "two-leaves" ? Leaf("data2", "m4/study/dm.xpt") : "") +
                (scenario == "other-stf" ? Leaf("other-stf", "m4/study/other.xml") : "") +
                "</m4-3-literature-references></m4-nonclinical-study-reports></ectd:ectd>");
            var references = (scenario == "define-not-referenced" ? "" : Doc("define", "data-tabulation-data-definition")) +
                (scenario is "unreferenced" or "other-stf" or "unknown-reference" ? "" : Doc("data", scenario == "wrong-tag" ? "study-report-body" : "data-tabulation-dataset-send")) +
                (scenario == "two-leaves" ? Doc("data2", "data-tabulation-dataset-send") : "") +
                (scenario == "repeated-reference" ? Doc("data", "data-tabulation-dataset-send") : "") +
                (scenario is "unknown-reference" or "known-with-unknown" ? Doc("absent", "study-report-body") : "");
            _files[StfPath] = Encoding.UTF8.GetBytes(Stf(references));
            if (scenario == "other-stf") _files["0001/m4/study/other.xml"] = Encoding.UTF8.GetBytes(Stf(Doc("data", "data-tabulation-dataset-send")));
            var define = XDocument.Parse(PackageDefineXmlReaderTests.Sample("SENDIG"));
            XNamespace odm = "http://www.cdisc.org/ns/odm/v1.3", def = "http://www.cdisc.org/ns/def/v2.1";
            var group = define.Descendants(odm + "ItemGroupDef").Single();
            if (scenario == "different-name") group.SetAttributeValue("Name", "OTHER");
            if (scenario == "different-sas-name") group.SetAttributeValue("SASDatasetName", "OTHER");
            if (scenario == "missing-name") group.SetAttributeValue("Name", null);
            if (scenario == "empty-name") group.SetAttributeValue("Name", "");
            if (scenario == "missing-sas-name") group.SetAttributeValue("SASDatasetName", null);
            if (scenario == "empty-sas-name") group.SetAttributeValue("SASDatasetName", "");
            if (scenario == "lower-name") group.SetAttributeValue("Name", "dm");
            if (scenario == "space-name") group.SetAttributeValue("Name", " DM ");
            if (scenario == "lower-sas-name") group.SetAttributeValue("SASDatasetName", "dm");
            if (scenario == "space-sas-name") group.SetAttributeValue("SASDatasetName", " DM ");
            if (scenario == "missing-oid") group.SetAttributeValue("OID", null);
            if (scenario == "empty-oid") group.SetAttributeValue("OID", "");
            if (scenario == "duplicate-oid")
            {
                var duplicate = new XElement(group); duplicate.Element(def + "leaf")!.Remove();
                group.AddAfterSelf(duplicate);
            }
            if (scenario != "missing-define") _files["0001/m4/study/define.xml"] = Encoding.UTF8.GetBytes(define.ToString() + (scenario == "bad-define" ? "<broken" : ""));
            var columns = new[] { new PackageXptReaderTests.Column(scenario == "missing-study-column" ? "OTHER" : "STUDYID", 2, scenario == "possible-row" ? 24 : 80) };
            var bytes = PackageXptReaderTests.Sample(columns, [PackageXptReaderTests.TextRow(columns, scenario == "different-study" ? "B" : scenario == "empty-study" ? "" : scenario == "undecoded-study" ? "研究" : "A")]);
            Encoding.ASCII.GetBytes((scenario == "different-member" ? "OTHER" : "DM").PadRight(8)).CopyTo(bytes, 408);
            if (scenario != "missing-data") _files["0001/m4/study/dm.xpt"] = scenario == "bad-data" ? Encoding.ASCII.GetBytes(new string('x', 80)) : bytes;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Leaf(string id, string href) => "<leaf ID='" + id + "' operation='new' checksum='00000000000000000000000000000000' checksum-type='md5' xlink:href='" + href + "'><title>" + id + "</title></leaf>";
        private static string Doc(string id, string tag) => "<doc-content xlink:href='../../index.xml#" + id + "'><file-tag name='" + tag + "' info-type='ich'/></doc-content>";
        private static string Stf(string references) => "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'>" +
            "<study-identifier><title>Study</title><study-id>A</study-id></study-identifier><study-document>" + references + "</study-document></ectd:study>";
        public bool Armed { get; set; }
        public Action? CancelRead { get; set; }
        public List<string> Opens { get; } = [];
        public string SourceId => _scenario == "identity-source" && _verifications >= 8 ? "changed" : "stf-define-fixture";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits SelectedLimits { get; set; } = new();
        public PackageReadLimits Limits => SelectedLimits;
        public Stream OpenRead(string path)
        {
            if (Armed)
            {
                if (path != "0001/m4/study/define.xml" && path != "0001/m4/study/dm.xpt") throw new InvalidOperationException("Frozen XML must not be reopened.");
                Opens.Add(path);
                CancelRead?.Invoke();
                if (path.EndsWith("/dm.xpt", StringComparison.Ordinal))
                {
                    if (_scenario == "unreadable-data") throw new IOException("Simulated unreadable dataset.");
                    if (_scenario == "hash") return new MemoryStream([.. _files[path][..^1], 0], false);
                }
            }
            return new MemoryStream(_files[path], false);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Armed && ++_verifications == 8)
            {
                if (_scenario == "final-change") throw new PackageInputChangedException(SourceId);
                if (_scenario == "final-io") throw new IOException("Final verification unavailable.");
            }
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
