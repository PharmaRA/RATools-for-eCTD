using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaXfaTemplateEvidenceTests
{
    [Theory]
    [InlineData("2252", true)]
    [InlineData("2253", true)]
    [InlineData("3674", true)]
    [InlineData("3938", true)]
    [InlineData("3988", true)]
    [InlineData("356h", false)]
    [InlineData("1571", false)]
    public async Task OfficialTemplateMatchIsIndependentOfFileNameAndNotFormQualification(string form, bool expected)
    {
        var fixture = await Fixture(form);
        var result = await new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var document = Assert.Single(result.Documents);
        Assert.Null(document.Facts!.Basics.ParseError);
        if (expected)
        {
            Assert.True(document.Facts.Basics.IsEncrypted);
            Assert.Equal(PdfContentProcessingStatus.Processed, document.Facts.ContentProcessing);
            Assert.True(document.Facts.Form.Xfa.ReadComplete);
        }
        var matches = result.FormTemplates;
        Assert.Equal(expected ? 1 : 0, matches.Count);
        if (expected)
        {
            var match = Assert.Single(matches);
            Assert.Equal("fda-form-" + form, match.SourceFormId);
            Assert.Equal(document.LogicalPath, match.LogicalPath);
            Assert.Equal(document.CapturedSha256, match.CapturedSha256);
            Assert.Equal(fixture.Context.SnapshotId, match.StandardsSnapshotId);
            Assert.Equal("fda-xfa-template-equality-v1", match.AlgorithmVersion);
            Assert.False(document.Facts.Form.Complete);
        }
        Assert.DoesNotContain(result.Checks, check => check.RuleId == "FDA47-5");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("inventory")]
    [InlineData("inventory-changed")]
    [InlineData("source-address")]
    [InlineData("legacy")]
    public async Task MissingOrChangedArchivedEvidenceCannotBorrowCurrentTemplate(string scenario)
    {
        var fixture = await Fixture("2252", sourceScenario: scenario);
        var result = await new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.True(Assert.Single(result.Documents).Facts!.Form.Xfa.ReadComplete);
        Assert.Empty(result.FormTemplates);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("historical")]
    [InlineData("legacy-processing")]
    [InlineData("parse")]
    [InlineData("unreadable-encryption")]
    [InlineData("pages")]
    [InlineData("acroform")]
    [InlineData("no-xfa")]
    [InlineData("incomplete")]
    [InlineData("reason")]
    [InlineData("duplicate")]
    [InlineData("no-template")]
    [InlineData("no-datasets")]
    [InlineData("unknown-template")]
    [InlineData("bad-packet-hash")]
    [InlineData("zero-bytes")]
    [InlineData("excess-bytes")]
    [InlineData("single-xdp")]
    public async Task UnboundOrContradictoryFactsDoNotEstablishTemplateEquality(string scenario)
    {
        var fixture = await Fixture("2252");
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/unclassified.pdf");
        using var stream = fixture.Input.Packages.OpenRead(file.LogicalPath);
        var original = new PdfPigPackageInspector().Inspect(stream, file.LogicalPath, new(), CancellationToken.None);
        Assert.True(original.Form.Xfa.ReadComplete);
        var packets = original.Form.Xfa.Packets.ToList();
        var template = packets.Single(item => item.Name == "template");
        if (scenario == "duplicate") packets.Add(template);
        if (scenario == "no-template") packets = packets.Where(item => item.Name != "template").ToList();
        if (scenario == "no-datasets") packets = packets.Where(item => item.Name != "datasets").ToList();
        if (scenario is "unknown-template" or "bad-packet-hash" or "zero-bytes" or "excess-bytes")
            packets[packets.IndexOf(template)] = template with { Sha256 = scenario == "unknown-template" ? new string('0', 64) : scenario == "bad-packet-hash" ? "bad" : template.Sha256,
                DecodedBytes = scenario == "zero-bytes" ? 0 : scenario == "excess-bytes" ? long.MaxValue : template.DecodedBytes };
        if (scenario == "single-xdp") packets.Add(new("xdp", new string('0', 64), 1));
        var xfa = new PackageXfaFacts(scenario != "incomplete", packets, original.Form.Xfa.Data, scenario == "reason" ? ["inconsistent"] : []);
        var form = new PackagePdfFormFacts(scenario == "acroform" ? null : true, scenario != "no-xfa", false, [], xfa: xfa);
        var basics = original.Basics with { ParseError = scenario == "parse" ? "opaque" : null, PageCount = scenario == "pages" ? null : original.Basics.PageCount };
        var facts = new PackagePdfFacts(basics, [], [], false, false, form: form,
            contentProcessing: scenario == "legacy-processing" ? PdfContentProcessingStatus.Unknown : scenario == "unreadable-encryption" ? PdfContentProcessingStatus.Encrypted : PdfContentProcessingStatus.Processed);
        var run = new FdaXfaTemplateEvidence.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(scenario == "historical" ? "0000/unclassified.pdf" : file.LogicalPath, facts, scenario == "hash" ? new string('0', 64) : file.Sha256));
        Assert.Empty(run.Matches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsOnlyPreviouslyEstablishedMatches(bool afterMatch)
    {
        var fixture = await Fixture("2252");
        var document = Assert.Single((await new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files)).Documents);
        using var cancellation = new CancellationTokenSource();
        var run = new FdaXfaTemplateEvidence.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token);
        if (afterMatch) { run.Observe(document); run.Observe(document); }
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => run.Observe(document));
        Assert.Equal(afterMatch ? 1 : 0, run.Matches.Count);
    }

    [Fact]
    public async Task ExhaustedIndependentBudgetDoesNotPublishPartialMatch()
    {
        var fixture = await Fixture("2252", limits: new() { MaxPdfObjects = 1 });
        var run = new FdaXfaTemplateEvidence.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        using var stream = fixture.Input.Packages.OpenRead("0001/unclassified.pdf");
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/unclassified.pdf", new(), CancellationToken.None);
        run.Observe(new("0001/unclassified.pdf", facts, fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/unclassified.pdf").Sha256));
        Assert.Empty(run.Matches);
    }

    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string form, string? sourceScenario = null, PackageReadLimits? limits = null)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider(),
            clock: new Clock(sourceScenario == "legacy" ? 25 : 26)).CaptureAsync("us-fda-ectd-3.2.2");
        if (sourceScenario is "source" or "inventory" or "inventory-changed" or "source-address")
        {
            var source = snapshot.Resources.Single(item => item.Key == "official-fda-form-2252");
            var resources = snapshot.Resources.Where(item => sourceScenario == "source" ? item.Key != source.Key : sourceScenario is "inventory" or "inventory-changed" ? item.Key != "official-fda-form-inventory" : item.Key != source.Key).ToList();
            if (sourceScenario == "inventory-changed") resources.Add(StandardsSnapshotResource.Create("official-fda-form-inventory", "SourceInventory", "metadata/official-sources/forms-2026-11-20.json", "application/json", "{}"u8.ToArray()));
            if (sourceScenario == "source-address") resources.Add(StandardsSnapshotResource.Create(source.Key, source.Category, "official-sources/moved.pdf", source.MediaType, source.ReadBytes()));
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with
            { Provenance = snapshot.Descriptor.Provenance.Where(item => sourceScenario != "source" || item.Sha256 != source.Sha256).ToArray() }, resources, snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new DateOnly(2026, 11, 26));
        var root = Environment.GetEnvironmentVariable("RATOOLS_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("Repository root required.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "reference/publisher/official-sources/2026-11-20-forms/fda-form-" + form + ".pdf"));
        var capture = new Capture(bytes, limits ?? new());
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(byte[] pdf, PackageReadLimits limits)
        {
            Limits = limits;
            var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            _files["0001/unclassified.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5-template";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Clock(int day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, day, 12, 0, 0, TimeSpan.Zero);
    }
}
