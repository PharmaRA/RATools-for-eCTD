using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageStudyDataInspectorTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("tag-candidate")]
    [InlineData("history")]
    public async Task ActualStfLeafCaptureAndTsFactsAreBoundWithoutRereadingXml(string scenario)
    {
        var fixture = await Fixture(scenario);
        var result = await Inspect(fixture);
        Assert.True(result.Complete);
        var document = Assert.Single(result.Documents);
        Assert.Single(document.Stf.Owners);
        var reference = Assert.Single(document.References);
        Assert.Equal(StudyDataReferenceStatus.Read, reference.Status);
        Assert.Equal("content", reference.Reference.Target!.LeafId);
        Assert.StartsWith(scenario == "history" ? "0000/" : "0001/", reference.ContentPath, StringComparison.Ordinal);
        Assert.Equal(TrialSummaryStatus.Read, reference.TrialSummary!.Status);
        Assert.Equal("A", Assert.Single(reference.TrialSummary.Rows).StudyId);
        Assert.Equal("STSTDTC", reference.TrialSummary.Rows[0].Parameter);
        Assert.Equal("2020-01-01", reference.TrialSummary.Rows[0].Value);
        Assert.Equal(1, fixture.Packages.Sequences.Values.Cast<Capture>().Sum(input => input.DataReads));
        Assert.Equal(64, result.SelectionDigest.Length);
    }

    [Theory]
    [InlineData("missing-leaf", "ReferenceUnavailable")]
    [InlineData("missing-file", "MissingContent")]
    [InlineData("missing-history", "ReferenceUnavailable")]
    [InlineData("future-content", "InvalidContentReference")]
    [InlineData("unavailable", "Unavailable")]
    [InlineData("changed-bytes", "InputChanged")]
    [InlineData("not-candidate", "NotSelected")]
    public async Task MissingUnqualifiedAndUnselectedReferencesStayDistinct(string scenario, string expected)
    {
        var result = await Inspect(await Fixture(scenario));
        var reference = Assert.Single(Assert.Single(result.Documents).References);
        Assert.Equal(expected, reference.Status.ToString());
        Assert.Null(reference.TrialSummary);
        Assert.Null(reference.Xpt);
        if (scenario != "not-candidate") Assert.False(result.Complete);
    }

    [Fact]
    public async Task FileNameOnlyDiscoversCandidateAndCannotProveTsContent()
    {
        var result = await Inspect(await Fixture("fake-xpt"));
        var reference = Assert.Single(Assert.Single(result.Documents).References);
        Assert.Equal(StudyDataReferenceStatus.Read, reference.Status);
        Assert.Equal(XptReadStatus.Unsupported, reference.Xpt!.Content!.Status);
        Assert.Equal(TrialSummaryStatus.Unavailable, reference.TrialSummary!.Status);
        Assert.False(result.Complete);
    }

    [Fact]
    public async Task DistinctStfsWithSameStudyIdStaySeparateAndSharedFileIsReadOnce()
    {
        var fixture = await Fixture("two-stfs");
        var result = await Inspect(fixture);
        Assert.True(result.Complete);
        Assert.Equal(2, result.Documents.Count);
        Assert.Equal(2, result.Documents.Select(document => document.Stf.Document.LogicalPath).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, fixture.Target.DataReads);
        Assert.Same(result.Documents[0].References[0].Xpt, result.Documents[1].References[0].Xpt);
    }

    [Fact]
    public async Task AggregateByteBudgetIsSharedAcrossFiles()
    {
        var fixture = await Fixture("two-files");
        var result = await PackageStudyDataInspector.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context,
            limits: new(MaxBytes: fixture.Target.DatasetLength));
        var references = Assert.Single(result.Documents).References;
        Assert.Equal(2, references.Count);
        Assert.Single(references, reference => reference.Status == StudyDataReferenceStatus.Read);
        Assert.Single(references, reference => reference.Status == StudyDataReferenceStatus.LimitExceeded);
        Assert.False(result.Complete);
        Assert.Equal(1, fixture.Target.DataReads);
    }

    [Fact]
    public async Task ChangedPackageCannotPublishEarlierPartialData()
    {
        var result = await Inspect(await Fixture("changed-after"));
        Assert.False(result.Complete);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public async Task ExplicitCancellationPropagatesAndSelectionIdentityIncludesEncoding()
    {
        var fixture = await Fixture("valid");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageStudyDataInspector.InspectAsync(fixture.Packages,
            fixture.Lifecycle, fixture.Context, cancellationToken: cancellation.Token));
        var original = await Inspect(fixture);
        var encoded = await PackageStudyDataInspector.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context, XptTextEncoding.Utf8);
        Assert.NotEqual(original.SelectionDigest, encoded.SelectionDigest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyReferenceListDoesNotBypassDocumentQualification(bool invalid)
    {
        var fixture = await Fixture("empty-stf");
        if (invalid) fixture = WithFacts(fixture, invalidStf: true);
        var graph = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.True(graph.TargetScopeComplete);
        var bound = Assert.Single(graph.Documents);
        Assert.Single(bound.Owners);
        Assert.Empty(bound.References);
        Assert.Equal(!invalid, bound.StructureComplete);
        var result = await Inspect(fixture);
        Assert.Equal(!invalid, result.Complete);
        Assert.Empty(Assert.Single(result.Documents).References);
    }

    [Fact]
    public async Task WorkInterruptionCannotPublishEarlierUnreverifiedContent()
    {
        var fixture = await Fixture("wide-work");
        fixture.Target.SelectedLimits = new() { MaxXmlNodes = 4000 };
        fixture = WithFacts(fixture);
        var graph = PackageStfBindingReader.Read(fixture.Packages, fixture.Lifecycle, fixture.Context, default);
        Assert.True(graph.TargetScopeComplete, "The fixture budget must permit complete association discovery.");
        fixture.Target.ChangeAtFinalVerification = true;
        var result = await Inspect(fixture);
        Assert.True(fixture.Target.DataReads > 0, "Work must reach real data before interruption.");
        Assert.False(result.Complete);
        Assert.Empty(result.Documents); // Work interruption must not leak earlier facts without final qualification.
    }

    private static FixtureData WithFacts(FixtureData fixture, bool invalidStf = false)
    {
        var inspections = fixture.Lifecycle.XmlInspections.Select(xml => new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId,
            xml.AssetManifestDigest, fixture.Target.Limits.Digest(), xml.Backbones, xml.Checks, xml.RulesDigest,
            invalidStf ? new PackageStfInspection(true, xml.StudyTaggingFiles.Documents.Select(stf => new ParsedStudyTaggingFile(stf.LogicalPath,
                stf.CapturedSha256, stf.ReadComplete, CheckStatus.Fail, stf.SystemIdentifier, stf.Elements, stf.ProcessingInstructions, stf.ResolvedAssets))) : xml.StudyTaggingFiles));
        return fixture with { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, inspections,
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };
    }

    private static Task<PackageStudyDataInspection> Inspect(FixtureData fixture) =>
        PackageStudyDataInspector.InspectAsync(fixture.Packages, fixture.Lifecycle, fixture.Context);
    private sealed record FixtureData(Capture Target, PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = PackageStandardsContext.Current;
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var historical = scenario == "history" ? new Capture(app, "0000", "valid") : null;
        var packages = new PackageInputSet(target, new(app, "0001", historical is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, historical.SourceId, historical.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]),
            historical is null ? [] : [historical]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        foreach (var xml in lifecycle.XmlInspections)
        {
            Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, backbone => backbone.Kind == BackboneKind.Ich).DtdStatus);
            Assert.All(xml.StudyTaggingFiles.Documents, stf => Assert.Equal(CheckStatus.Pass, stf.DtdStatus));
        }
        foreach (var input in packages.Sequences.Values.Cast<Capture>()) input.Armed = true;
        return new(target, packages, lifecycle, context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _scenario;
        private int _verifications;
        public Capture(Guid app, string sequence, string scenario)
        {
            _scenario = scenario;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            {
                using var source = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray();
            }, StringComparer.Ordinal);
            var contentName = scenario is "tag-candidate" or "not-candidate" ? "m4/study/data.bin" : "m4/study/ts.xpt";
            var contentHref = scenario == "future-content" ? "../0002/m4/study/ts.xpt" : contentName;
            var secondOwner = scenario is "two-stfs" or "wide-work" ? Leaf("stf-b", scenario == "wide-work" ? "m4/study/z-stf.xml" : "m4/study/stf-b.xml") : "";
            var secondContent = scenario == "two-files" ? Leaf("content-b", "m4/study/other.xpt") : scenario == "wide-work" ?
                string.Join("", Enumerable.Range(0, 40).Select(i => Leaf("wide-" + i, "m4/study/wide-" + i + ".xpt"))) : "";
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:ectd SYSTEM 'util/dtd/ich-ectd-3-2.dtd'>" +
                "<ectd:ectd xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3c.org/1999/xlink' dtd-version='3.2'>" +
                "<m1-administrative-information-and-prescribing-information>" + Leaf("regional", "m1/us/us-regional.xml") + "</m1-administrative-information-and-prescribing-information>" +
                "<m4-nonclinical-study-reports><m4-3-literature-references>" + Leaf("stf", "m4/study/stf.xml") + secondOwner +
                Leaf("content", contentHref) + secondContent + "</m4-3-literature-references></m4-nonclinical-study-reports></ectd:ectd>");
            var reference = scenario is "history" or "missing-history" ? "../../../0000/index.xml#content" : "../../index.xml#" + (scenario == "missing-leaf" ? "absent" : "content");
            var tag = scenario == "not-candidate" ? "study-report-body" : "data-tabulation-dataset-send";
            var secondReference = scenario == "two-files" ? Doc("../../index.xml#content-b", tag) : "";
            var stfText = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'>" +
                "<study-identifier><title>Study</title><study-id>A</study-id></study-identifier><study-document>" +
                (scenario == "empty-stf" ? "" : Doc(reference, tag) + secondReference) + "</study-document></ectd:study>";
            _files[sequence + "/m4/study/stf.xml"] = Encoding.UTF8.GetBytes(stfText);
            if (scenario == "two-stfs") _files[sequence + "/m4/study/stf-b.xml"] = Encoding.UTF8.GetBytes(stfText);
            if (scenario == "wide-work") _files[sequence + "/m4/study/z-stf.xml"] = Encoding.UTF8.GetBytes(stfText.Replace(Doc(reference, tag),
                string.Join("", Enumerable.Range(0, 40).Select(i => Doc("../../index.xml#wide-" + i, tag))), StringComparison.Ordinal));
            var columns = new[] { new PackageXptReaderTests.Column("STUDYID", 2, 24), new("TSPARMCD", 2, 16), new("TSVAL", 2, 40) };
            var dataset = PackageXptReaderTests.Sample(columns, [PackageXptReaderTests.TextRow(columns, "A", "STSTDTC", "2020-01-01")]);
            DatasetLength = dataset.Length;
            if (scenario != "missing-file") _files[sequence + "/" + contentName] = scenario == "fake-xpt" ? Encoding.ASCII.GetBytes(new string('x', 80)) : dataset;
            if (scenario == "two-files") _files[sequence + "/m4/study/other.xpt"] = dataset;
            if (scenario == "wide-work")
            {
                for (var i = 0; i < 40; i++) _files[sequence + "/m4/study/wide-" + i + ".xpt"] = dataset;
                for (var i = 0; i < 200; i++) _files[sequence + "/m4/study/filler-" + i + ".txt"] = "unused"u8.ToArray();
            }
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static string Leaf(string id, string href) => "<leaf ID='" + id + "' operation='new' checksum='00000000000000000000000000000000' checksum-type='md5' xlink:href='" + href + "'><title>" + id + "</title></leaf>";
        private static string Doc(string href, string tag) => "<doc-content xlink:href='" + href + "'><file-tag name='" + tag + "' info-type='ich'/></doc-content>";
        public bool Armed { get; set; }
        public bool ChangeAtFinalVerification { get; set; }
        public PackageReadLimits SelectedLimits { get; set; } = new();
        public int DataReads { get; private set; }
        public int DatasetLength { get; }
        public PackageInputManifest Manifest { get; }
        public string SourceId => "study-data-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits => SelectedLimits;
        public Stream OpenRead(string path)
        {
            if (Armed)
            {
                if (path.EndsWith(".xml", StringComparison.Ordinal)) throw new InvalidOperationException("Frozen XML must not be reopened.");
                DataReads++;
                if (_scenario == "unavailable") throw new IOException("Simulated dataset read failure.");
            }
            var bytes = _files[path].ToArray();
            if (Armed && _scenario == "changed-bytes") bytes[^1] ^= 1;
            return new MemoryStream(bytes, false);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Armed && ChangeAtFinalVerification && DataReads > 0 && !new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(PackageCapturedXptReader), StringComparison.Ordinal) == true))
                throw new PackageInputChangedException(SourceId);
            if (Armed && _scenario == "changed-after" && ++_verifications >= 4) throw new PackageInputChangedException(SourceId);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
