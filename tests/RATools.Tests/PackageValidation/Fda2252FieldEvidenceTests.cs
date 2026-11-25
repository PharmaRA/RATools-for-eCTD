using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class Fda2252FieldEvidenceTests
{
    private const string Number = "db_aplcnt_nmbr";
    private const string Type = "db_appl_type";
    private const string Continuation = "db_aplcnt_nmbr_2";

    [Theory]
    [InlineData("blank", "Empty", "")]
    [InlineData("filled", "Text", "000123")]
    [InlineData("missing", "Missing", null)]
    [InlineData("duplicate", "Unknown", null)]
    [InlineData("second", "Unknown", null)]
    [InlineData("complex", "Unknown", null)]
    [InlineData("namespace", "Unknown", null)]
    [InlineData("whitespace", "Text", " 000123 ")]
    [InlineData("ancestor-missing", "Unknown", null)]
    [InlineData("ancestor-duplicate", "Unknown", null)]
    [InlineData("ancestor-no-children", "Unknown", null)]
    [InlineData("ancestor-text", "Unknown", null)]
    [InlineData("wrong-root", "Unknown", null)]
    [InlineData("metadata-conflict", "Unknown", null)]
    [InlineData("orphan-child", "Unknown", null)]
    public async Task RawFieldStateDoesNotInventApplicationIdentity(string scenario, string state, string? text)
    {
        var fixture = await Fixture(scenario);
        var result = await new PackagePdfValidator(new FactsInspector(fixture.Facts), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Single(result.FormTemplates);
        var evidence = Assert.Single(result.FormFieldEvidence);
        Assert.Equal(state, evidence.ApplicationNumber.State.ToString());
        Assert.Equal(text, evidence.ApplicationNumber.RawText);
        Assert.Equal(Assert.Single(result.FormTemplates), evidence.Template);
        Assert.Equal("fda2252-raw-datasets-v1", evidence.AlgorithmVersion);
        if (scenario is "filled" or "whitespace")
        {
            Assert.Equal("NDA", evidence.ApplicationType.RawText);
            Assert.Equal("0042", evidence.ContinuationNumber.RawText);
        }
        Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-5");
    }

    [Fact]
    public async Task RealOfficialBlankPdfRetainsPlaceholderAndEmptyNumbers()
    {
        var fixture = await Fixture("blank");
        var result = await new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var evidence = Assert.Single(result.FormFieldEvidence);
        Assert.Equal(PackageFormRawFieldState.Text, evidence.ApplicationType.State);
        Assert.Equal("Choose One", evidence.ApplicationType.RawText);
        Assert.Equal(PackageFormRawFieldState.Empty, evidence.ApplicationNumber.State);
        Assert.Equal(PackageFormRawFieldState.Empty, evidence.ContinuationNumber.State);
        Assert.Equal(Assert.Single(Assert.Single(result.Documents).Facts!.Form.Xfa.Packets, item => item.Name == "datasets").Sha256, evidence.DatasetsSha256);
        Assert.False(Assert.Single(result.Documents).Facts!.Form.Complete);
    }

    [Theory]
    [InlineData("unknown-template")]
    [InlineData("incomplete")]
    [InlineData("missing-datasets")]
    public async Task WithoutQualifiedTemplateNoFieldsArePublished(string scenario)
    {
        var fixture = await Fixture(scenario);
        var result = await new PackagePdfValidator(new FactsInspector(fixture.Facts), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Empty(result.FormTemplates);
        Assert.Empty(result.FormFieldEvidence);
    }

    [Theory]
    [InlineData("type-missing", "Missing", null, "Text", "0042")]
    [InlineData("type-duplicate", "Unknown", null, "Text", "0042")]
    [InlineData("suffix-missing", "Text", "NDA", "Missing", null)]
    [InlineData("suffix-complex", "Text", "NDA", "Unknown", null)]
    public async Task ApplicationFieldsKeepIndependentRawStates(string scenario, string typeState, string? typeText, string suffixState, string? suffixText)
    {
        var fixture = await Fixture(scenario);
        var result = await new PackagePdfValidator(new FactsInspector(fixture.Facts), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var evidence = Assert.Single(result.FormFieldEvidence);
        Assert.Equal(typeState, evidence.ApplicationType.State.ToString());
        Assert.Equal(typeText, evidence.ApplicationType.RawText);
        Assert.Equal(suffixState, evidence.ContinuationNumber.State.ToString());
        Assert.Equal(suffixText, evidence.ContinuationNumber.RawText);
        Assert.Equal("000123", evidence.ApplicationNumber.RawText);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("nodes")]
    [InlineData("characters")]
    [InlineData("capture")]
    [InlineData("template")]
    [InlineData("path")]
    public async Task IncompleteScanOrMismatchedBindingDoesNotPublishPartialFields(string scenario)
    {
        var fixture = await Fixture("filled");
        var result = await new PackagePdfValidator(new FactsInspector(fixture.Facts), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var document = Assert.Single(result.Documents); var match = Assert.Single(result.FormTemplates);
        if (scenario == "capture") match = match with { CapturedSha256 = new string('0', 64) };
        if (scenario == "template") match = match with { TemplateSha256 = new string('0', 64) };
        if (scenario == "path") match = match with { LogicalPath = "0000/unclassified.pdf" };
        var limits = new PackageReadLimits { MaxXmlNodes = scenario == "nodes" ? 1 : 500_000, MaxXmlCharacters = scenario == "characters" ? 1 : 16L * 1024 * 1024 };
        var work = scenario == "work" ? fixture.Facts.Form.Xfa.Packets.Count + 1 : 10000;
        Assert.Null(Fda2252FieldEvidence.Read(document, match, limits, ref work, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationDoesNotPublishAFieldRecord()
    {
        var fixture = await Fixture("filled");
        var result = await new PackagePdfValidator(new FactsInspector(fixture.Facts), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var document = Assert.Single(result.Documents); var match = Assert.Single(result.FormTemplates); var work = 10000;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Fda2252FieldEvidence.Read(document, match, new(), ref work, cancellation.Token));
    }

    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context, PackagePdfFacts Facts);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider(), clock: new Clock()).CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new(2026, 11, 26));
        var root = Environment.GetEnvironmentVariable("RATOOLS_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("Repository root required.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "reference/publisher/official-sources/2026-11-20-forms/fda-form-2252.pdf"));
        using var stream = new MemoryStream(bytes);
        var original = new PdfPigPackageInspector().Inspect(stream, "0001/unclassified.pdf", new(), CancellationToken.None);
        Assert.True(original.Form.Xfa.ReadComplete);
        var data = original.Form.Xfa.Data.ToList();
        var number = Assert.Single(data, item => item.LocalName == Number);
        var application = Assert.Single(data, item => item.LocalName == "application_info");
        if (scenario == "missing") data = data.Where(item => item != number).ToList();
        else if (scenario == "duplicate") data.Add(number);
        else if (scenario == "second") data.Add(number with { NodePath = number.NodePath[..^3] + "[2]" });
        else if (scenario == "complex") data[data.IndexOf(number)] = number with { HasChildren = true };
        else if (scenario == "namespace") data[data.IndexOf(number)] = number with { NamespaceUri = "urn:other", NodePath = number.NodePath.Replace("/{}" + Number, "/{urn%3Aother}" + Number, StringComparison.Ordinal) };
        else if (scenario is "filled" or "whitespace" or "type-missing" or "type-duplicate" or "suffix-missing" or "suffix-complex")
        {
            data[data.IndexOf(number)] = number with { DirectText = scenario == "whitespace" ? " 000123 " : "000123" };
            for (var i = 0; i < data.Count; i++)
                if (data[i].LocalName is Type or Continuation) data[i] = data[i] with { DirectText = data[i].LocalName == Type ? "NDA" : "0042" };
        }
        if (scenario == "ancestor-missing") data = data.Where(item => item != application).ToList();
        if (scenario == "ancestor-duplicate") data.Add(application with { NodePath = application.NodePath[..^3] + "[2]" });
        if (scenario == "ancestor-no-children") data[data.IndexOf(application)] = application with { HasChildren = false };
        if (scenario == "ancestor-text") data[data.IndexOf(application)] = application with { DirectText = "unexpected" };
        if (scenario == "wrong-root") data = data.Select(item => item with { NodePath = item.NodePath.Replace("form_2252[1]", "unrelated[1]", StringComparison.Ordinal) }).ToList();
        if (scenario == "metadata-conflict") data[data.IndexOf(number)] = number with { LocalName = "other" };
        if (scenario == "orphan-child") data.Add(new(number.NodePath + "/{}nested[1]", "nested", "", "value", false));
        if (scenario == "type-missing") data = data.Where(item => item.LocalName != Type).ToList();
        if (scenario == "type-duplicate") { var type = data.Single(item => item.LocalName == Type); data.Add(type with { NodePath = type.NodePath[..^3] + "[2]" }); }
        if (scenario == "suffix-missing") data = data.Where(item => item.LocalName != Continuation).ToList();
        if (scenario == "suffix-complex") { var suffix = data.Single(item => item.LocalName == Continuation); data[data.IndexOf(suffix)] = suffix with { HasChildren = true }; }
        var packets = original.Form.Xfa.Packets.Select(item => scenario == "unknown-template" && item.Name == "template" ? item with { Sha256 = new string('0', 64) } : item)
            .Where(item => scenario != "missing-datasets" || item.Name != "datasets");
        var form = new PackagePdfFormFacts(true, true, false, [], xfa: new(scenario != "incomplete", packets, data));
        var facts = new PackagePdfFacts(original.Basics, [], [], false, false, form: form, contentProcessing: PdfContentProcessingStatus.Processed);
        var capture = new Capture(bytes);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context, facts);
    }

    private sealed class FactsInspector(PackagePdfFacts facts) : IPackagePdfInspector
    {
        public string EngineVersion => "synthetic-xfa-fields-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken) => facts;
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(byte[] pdf)
        {
            var original = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            _files["0001/unclassified.pdf"] = pdf;
            Manifest = new(original.Manifest.ApplicationId, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5-fields";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    }
}
