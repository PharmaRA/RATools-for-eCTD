using System.IO.Compression;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using System.Text.Json;

namespace RATools.Tests.PackageValidation;

public sealed class FdaIdentityRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() => new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));
    private static readonly Guid App = Guid.Parse("13962af2-4b1c-4ab7-a76f-9c51bb08eef6");
    private static RuleCheckResult Check(PackageDeliveryInspection result, string id) => Assert.Single(result.Checks, item => item.RuleId == "FDA47-" + id);
    private static string Xml(string number = "123456", string sequence = "0001") => Fda47AttributeRulesTests.Valid()
        .Replace(">123456<", ">" + number + "<", StringComparison.Ordinal)
        .Replace("<sequence-number submission-sub-type=\"fdasst3\">0001</sequence-number>", "<sequence-number submission-sub-type=\"fdasst3\">" + sequence + "</sequence-number>", StringComparison.Ordinal);
    private static async Task<PackageDeliveryInspection> Inspect(ICapturedPackageInput input)
    {
        var set = new PackageInputSet(input, new HistoryBaselineManifest(App, "0001", []), []);
        return await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(2026, 11, 17))).InspectAsync(new(set), PackageValidationCatalog.UsProfile);
    }
    private sealed class Files : IDisposable
    {
        public string Root { get; } = RetainedPackageTestFiles.Create();
        public string Input => Path.Combine(Root, "input");
        public PackageInputReader Reader => new(Path.Combine(Root, "validation", "captures"));
        public Files(string application, string xml)
        {
            var memory = new PackageRegionalInspectionTests.MemoryInput(App, "0001", xml, PackageValidationCatalog.UsProfile);
            foreach (var file in memory.Manifest.Files)
            {
                var target = Path.Combine(Input, application, file.LogicalPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = memory.OpenRead(file.LogicalPath); using var destination = File.Create(target); source.CopyTo(destination);
            }
        }
        public string Zip(bool rootOnly = false)
        {
            var zip = Path.Combine(Root, "input.zip");
            ZipFile.CreateFromDirectory(rootOnly ? Path.Combine(Input, "123456", "0001") : Input, zip, CompressionLevel.NoCompression, false);
            return zip;
        }
        public void Dispose() => RetainedPackageTestFiles.RetainDirectory(Root);
    }

    [Theory]
    [InlineData(false, "123456", "0001", CheckStatus.Pass, CheckStatus.Pass)]
    [InlineData(true, "123456", "0001", CheckStatus.Pass, CheckStatus.Pass)]
    [InlineData(false, "654321", "0001", CheckStatus.Fail, CheckStatus.Pass)]
    [InlineData(true, "654321", "0001", CheckStatus.Fail, CheckStatus.Pass)]
    [InlineData(false, "123456", "0009", CheckStatus.Pass, CheckStatus.Fail)]
    [InlineData(true, "123456", "0009", CheckStatus.Pass, CheckStatus.Fail)]
    public async Task OwnerNumbersAreComparedToActualDirectoryAndZipLayout(bool zip, string number, string sequence, CheckStatus applicationStatus, CheckStatus sequenceStatus)
    {
        using var files = new Files("123456", Xml(number, sequence));
        await using var captured = await files.Reader.ReadAsync(new(App, "owned", zip ? files.Zip() : files.Input), new());
        var result = await Inspect(captured);
        Assert.Equal(applicationStatus, Check(result, "1519").Status); Assert.Equal(sequenceStatus, Check(result, "1714").Status);
    }

    [Fact]
    public async Task ApplicationDirectoryIsBoundEvenWhenNormalizedFileManifestMatches()
    {
        using var first = new Files("123456", Xml()); using var second = new Files("654321", Xml());
        await using var a = await first.Reader.ReadAsync(new(App, "same", first.Input), new());
        await using var b = await second.Reader.ReadAsync(new(App, "same", second.Input), new());
        Assert.Equal(a.Manifest.Digest, b.Manifest.Digest);
        Assert.NotEqual(a.InputDigest, b.InputDigest);
    }

    [Fact]
    public async Task RootOnlyZipCannotFabricateDirectoryEvidenceFromSelection()
    {
        using var files = new Files("123456", Xml());
        await using var captured = await files.Reader.ReadAsync(new(App, "owned", files.Zip(true), "0001"), new());
        var result = await Inspect(captured);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1519").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1714").Status);
    }

    [Theory]
    [InlineData("123456", "0001", CheckStatus.Pass)]
    [InlineData("123456", "0009", CheckStatus.Fail)]
    [InlineData("654321", "0009", CheckStatus.Pass)]
    public async Task GroupedRegulatoryActivitiesUseTheSameSequenceOnlyForTheSameApplication(string otherApplication, string otherSequence, CheckStatus expected)
    {
        var xml = XDocument.Parse(Xml()); var first = xml.Descendants("application").Single(); var other = new XElement(first);
        other.SetAttributeValue("application-containing-files", "false");
        other.Descendants("application-number").Single().Value = otherApplication;
        other.Descendants("sequence-number").Single().Value = otherSequence;
        first.AddAfterSelf(other);
        using var files = new Files("123456", xml.ToString());
        await using var captured = await files.Reader.ReadAsync(new(App, "owned", files.Input), new());
        var result = await Inspect(captured);
        Assert.Equal(expected, Check(result, "2033").Status);
        Assert.Equal(CheckStatus.Pass, Check(result, "1714").Status);
        Assert.DoesNotContain(result.Checks.Single(item => item.RuleId == "US-ADMIN-METADATA").Findings, item => item.Code == "US_SEQUENCE_MISMATCH");
    }

    [Fact]
    public async Task ExistingCaptureMetadataRetainsItsLegacyDigestAndDoesNotGainLayoutFacts()
    {
        using var files = new Files("123456", Xml());
        await using var legacy = await files.Reader.ReadAsync(new(App, "old", files.Input, "0001", CaptureSourceLayout: false), new());
        var input = new RegisteredPackageInput(Guid.NewGuid(), App, "0001", files.Input, null, legacy.SourceKind, legacy.InputDigest,
            "{\"capture\":{\"schemaVersion\":1}}", new(), DateTime.UtcNow, false);
        await using var replay = await files.Reader.ReadAsync(PackageInputSelection.FromRegistered(input), new());
        Assert.Equal(legacy.InputDigest, replay.InputDigest); Assert.Null(replay.SourceLayout);
        Assert.Equal(CheckStatus.NotEvaluated, Check(await Inspect(replay), "1519").Status);
        var current = input with { ManifestJson = "{\"capture\":{\"schemaVersion\":2}}" };
        await using var latest = await files.Reader.ReadAsync(PackageInputSelection.FromRegistered(current), new());
        Assert.NotEqual(legacy.InputDigest, latest.InputDigest);
        Assert.Equal("123456", latest.SourceLayout!.ApplicationDirectoryName);
        Assert.Equal("0001", latest.SourceLayout.SequenceDirectoryName);
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("multiple-owners")]
    [InlineData("ambiguous-number")]
    public async Task AmbiguousOwnerNeverChoosesAnArbitraryApplication(string change)
    {
        var xml = XDocument.Parse(Xml()); var first = xml.Descendants("application").Single();
        if (change == "missing-owner") first.SetAttributeValue("application-containing-files", "false");
        if (change == "multiple-owners") first.AddAfterSelf(new XElement(first));
        if (change == "ambiguous-number") first.Element("application-information")!.Add(new XElement("application-number", new XAttribute("application-type", "fdaat4"), "654321"));
        using var files = new Files("123456", xml.ToString());
        await using var captured = await files.Reader.ReadAsync(new(App, "owned", files.Input), new());
        var result = await Inspect(captured);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1519").Status);
    }

    [Fact]
    public async Task UnknownApplicationFolderNameIsNotParsedByStrippingArbitraryPrefixes()
    {
        using var files = new Files("nda123456", Xml());
        await using var captured = await files.Reader.ReadAsync(new(App, "owned", files.Input), new());
        Assert.Null(captured.SourceLayout!.ApplicationDirectoryName);
        Assert.Equal(CheckStatus.NotEvaluated, Check(await Inspect(captured), "1519").Status);
    }

    [Theory]
    [InlineData(2008, 3, 9, CheckStatus.NotApplicable)]
    [InlineData(2008, 3, 10, CheckStatus.Fail)]
    public async Task DirectoryRulesRespectTheirOwnEffectiveDate(int year, int month, int day, CheckStatus expected)
    {
        using var files = new Files("123456", Xml("654321"));
        await using var captured = await files.Reader.ReadAsync(new(App, "owned", files.Input), new());
        var inputs = new PackageInputSet(captured, new HistoryBaselineManifest(App, "0001", []), []);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(year, month, day))).InspectAsync(new(inputs), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Check(result, "1519").Status);
    }

    [Fact]
    public async Task TrailingSeparatorCannotEraseTheObservedApplicationDirectory()
    {
        using var files = new Files("123456", Xml("654321"));
        var path = Path.Combine(files.Input, "123456", "0001");
        await using var plain = await files.Reader.ReadAsync(new(App, "same", path), new());
        await using var trailing = await files.Reader.ReadAsync(new(App, "same", path + Path.DirectorySeparatorChar), new());
        Assert.Equal(plain.SourceLayout, trailing.SourceLayout);
        Assert.Equal(plain.InputDigest, trailing.InputDigest);
        Assert.Equal(CheckStatus.Fail, Check(await Inspect(trailing), "1519").Status);
    }
}
