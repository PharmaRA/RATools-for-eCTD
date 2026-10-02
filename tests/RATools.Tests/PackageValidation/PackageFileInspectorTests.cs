using System.IO.Compression;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageFileInspectorTests
{
    [Fact]
    public async Task FourSequencesUseRealCapturedBytesIncludingRegionalBackbonesAndHrefFreeDelete()
    {
        await using var fixture = new Fixture();
        var (input, lifecycle) = await fixture.SelectAsync("0003");
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.All(result.Checks.Where(check => check.RuleId is "FILE-HREF" or "FILE-MD5" or "INDEX-MD5" or "FILE-NAMING"), check => Assert.Equal(CheckStatus.Pass, check.Status));
        Assert.Equal(17, result.References.Count); // 13 payload leaves + 4 regional references; delete has no file.
        Assert.DoesNotContain(result.References, reference => reference.Location.LeafId == "beta-delete");
        Assert.All(result.References, reference => Assert.True(reference.Exists));
        Assert.Contains(Findings(result), finding => finding.Code == "STYLESHEET_POLICY_UNQUALIFIED");
    }

    [Theory]
    [InlineData("missing-file", "REFERENCED_FILE_MISSING")]
    [InlineData("changed-file", "FILE_CHECKSUM_MISMATCH")]
    [InlineData("checksum-format", "INVALID_FILE_CHECKSUM")]
    [InlineData("checksum-type", "INVALID_FILE_CHECKSUM")]
    [InlineData("index-hash", "INDEX_CHECKSUM_MISMATCH")]
    [InlineData("index-format", "INVALID_INDEX_CHECKSUM")]
    [InlineData("index-absent", "INDEX_CHECKSUM_FILE_MISSING")]
    [InlineData("missing-dtd", "DELIVERY_ASSET_MISSING")]
    [InlineData("changed-dtd", "DELIVERY_ASSET_CHANGED")]
    public async Task PhysicalDefectsAreLocatedIndependentlyOfWriterModels(string defect, string code)
    {
        await using var fixture = new Fixture();
        var pdf = fixture.PathOf("0000/m2/25-clin-over/overview.pdf");
        var hash = fixture.PathOf("0000/index-md5.txt");
        var dtd = fixture.PathOf("0000/util/dtd/ich-ectd-3-2.dtd");
        switch (defect)
        {
            case "missing-file": File.Delete(pdf); break;
            case "changed-file": File.AppendAllText(pdf, "\n% corruption"); break;
            case "checksum-format": fixture.Replace("0000/index.xml", "checksum=\"", "checksum=\"x"); break;
            case "checksum-type": fixture.Replace("0000/index.xml", "checksum-type=\"md5\"", "checksum-type=\"sha256\""); break;
            case "index-hash": File.WriteAllText(hash, new string('0', 32)); break;
            case "index-format": File.WriteAllText(hash, "bogus index.xml"); break;
            case "index-absent": File.Delete(hash); break;
            case "missing-dtd": File.Delete(dtd); break;
            case "changed-dtd": File.AppendAllText(dtd, "\n<!-- changed -->"); break;
        }
        var (input, lifecycle) = await fixture.SelectAsync("0000");
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.Contains(Findings(result), finding => finding.Code == code && finding.CheckStatus == CheckStatus.Fail && finding.Location.LogicalPath is not null);
        if (defect is "missing-dtd" or "changed-dtd")
            Assert.Equal(CheckStatus.Pass, lifecycle.XmlInspections[0].Backbones[0].DtdStatus); // Parsing uses pinned assets, delivery check uses submitted bytes.
    }

    [Theory]
    [InlineData("same", null)]
    [InlineData("changed-content", "DIRECTORY_ZIP_CONTENT_MISMATCH")]
    [InlineData("missing-path", "DIRECTORY_ZIP_PATH_MISMATCH")]
    [InlineData("empty-directory", "DIRECTORY_ZIP_EMPTY_DIRECTORY_MISMATCH")]
    [InlineData("extra-root", "ZIP_EXTRA_CONTENT")]
    public async Task RealZipParityComparesPathSetsAndBytes(string variant, string? expectedCode)
    {
        await using var fixture = new Fixture();
        fixture.CreateZip("0000", variant);
        var (input, lifecycle) = await fixture.SelectAsync("0000", comparison: true);
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        var parity = result.Checks.Single(check => check.RuleId == "ZIP-PARITY");
        Assert.Equal(expectedCode is null ? CheckStatus.Pass : CheckStatus.Fail, parity.Status);
        if (expectedCode is not null) Assert.Contains(parity.Findings, finding => finding.Code == expectedCode);
        Assert.NotEqual(result.PrimaryInputDigest, result.InputDigest);
        Assert.Equal(input.Comparison!.InputDigest, result.ComparisonInputDigest);
    }

    [Fact]
    public async Task RepackedIdenticalZipChangesTheCombinedBinding()
    {
        await using var fixture = new Fixture();
        fixture.CreateZip("0000", "same");
        var (first, _) = await fixture.SelectAsync("0000", comparison: true);
        fixture.CreateZip("0000", "same", "other.zip", CompressionLevel.NoCompression);
        var (second, _) = await fixture.SelectAsync("0000", comparison: true, archive: "other.zip");
        Assert.Equal(first.Comparison!.Manifest.Digest, second.Comparison!.Manifest.Digest);
        Assert.NotEqual(first.InputDigest, second.InputDigest);
    }

    [Theory]
    [InlineData("../0000/m2/25-clin-over/overview.pdf", "FILE_CHECKSUM_MISMATCH")]
    [InlineData("../0002/later.pdf", "FILE_REFERENCE_IN_FUTURE")]
    [InlineData("../../outside/file.pdf", "INVALID_FILE_REFERENCE")]
    [InlineData("https://example.invalid/file.pdf", "EXTERNAL_CONTENT_REFERENCE")]
    public async Task FileReferencesStayWithinExplicitHistoryAndNeverFetchExternalContent(string href, string code)
    {
        await using var fixture = new Fixture();
        fixture.Replace("0001/index.xml", "m3/32-body-data/32s-drug-sub/drug-a-alpha/32s4-contr-drug-sub/32s41-spec/specification.pdf", href);
        var (input, lifecycle) = await fixture.SelectAsync("0001");
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.Contains(Findings(result), finding => finding.Code == code);
        if (code == "FILE_CHECKSUM_MISMATCH") Assert.True(Assert.Single(result.References, reference => reference.Location.LeafId == "alpha-spec-v2").Exists);
    }

    [Fact]
    public async Task MissingHistoricalFileSourceLeavesChecksumNotEvaluated()
    {
        await using var fixture = new Fixture();
        fixture.Replace("0001/index.xml", "m3/32-body-data/32s-drug-sub/drug-a-alpha/32s4-contr-drug-sub/32s41-spec/specification.pdf", "../0000/old.pdf");
        var (input, lifecycle) = await fixture.SelectAsync("0001", history: []);
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.Contains(Findings(result), finding => finding.Code == "FILE_HISTORY_NOT_AVAILABLE");
        Assert.Equal(CheckStatus.NotEvaluated, result.Checks.Single(check => check.RuleId == "FILE-MD5").Status);
    }

    [Fact]
    public async Task DifferentContentBaselineCannotReuseACompletedLifecycleInspection()
    {
        await using var fixture = new Fixture();
        var (_, old) = await fixture.SelectAsync("0001");
        fixture.Replace("0000/index.xml", "Drug A", "Drug Different");
        var (newInput, _) = await fixture.SelectAsync("0001");
        await Assert.ThrowsAsync<ArgumentException>(() => new PackageFileInspector().InspectAsync(newInput, old));
    }

    [Fact]
    public async Task ChangedComparisonAndCancellationDoNotReturnReusableResults()
    {
        await using var fixture = new Fixture();
        fixture.CreateZip("0000", "same");
        var (input, lifecycle) = await fixture.SelectAsync("0000", comparison: true);
        File.AppendAllText(fixture.ArchivePath("package.zip"), "changed");
        await Assert.ThrowsAsync<PackageInputChangedException>(() => new PackageFileInspector().InspectAsync(input, lifecycle));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageFileInspector().InspectAsync(input, lifecycle, cancellation.Token));
    }

    [Fact]
    public async Task EmptyFoldersAndSurroundingFilesAreRecordedWithoutAdoptingThemAsHistory()
    {
        await using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.PathOf("0000/empty"));
        var (input, lifecycle) = await fixture.SelectAsync("0000", applicationRoot: true);
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.Contains(Findings(result), finding => finding.Code == "EMPTY_DELIVERY_DIRECTORY");
        Assert.Contains(Findings(result), finding => finding.Code == "SURROUNDING_INPUT_FILE");
        Assert.All(result.References, reference => Assert.StartsWith("0000/", reference.SourceLogicalPath));
    }

    [Theory]
    [InlineData("Bad_Name.pdf")]
    [InlineData("two.extensions.pdf")]
    [InlineData("missing-extension")]
    public async Task VerifiedIchNamingConstraintsDoNotDependOnRecommendedFolderSpellings(string name)
    {
        await using var fixture = new Fixture();
        File.WriteAllText(fixture.PathOf("0000/" + name), "extra");
        var (input, lifecycle) = await fixture.SelectAsync("0000");
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.Contains(Findings(result), finding => finding.Code == "ICH_FILE_NAME_INVALID" && finding.Location.LogicalPath == "0000/" + name);
    }

    private static IEnumerable<ValidationFinding> Findings(PackageFileInspection result) => result.Checks.SelectMany(check => check.Findings);

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-file-inspection-" + Guid.NewGuid().ToString("N"));
        private readonly List<ICapturedPackageInput> _captures = [];
        private readonly PackageInputReader _reader;
        private static readonly string[] HistoricalSequences = ["0000", "0001", "0002"];
        private static readonly Guid ApplicationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public Fixture()
        {
            var source = Path.Combine(PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/sequences");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = PathOf(Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            _reader = new(Path.Combine(_root, "captures"));
        }
        public string PathOf(string relative) => Path.Combine(_root, "source", relative);
        public string ArchivePath(string name) => Path.Combine(_root, name);
        public void Replace(string relative, string old, string replacement)
        {
            var path = PathOf(relative);
            var content = File.ReadAllText(path);
            Assert.Contains(old, content, StringComparison.Ordinal);
            File.WriteAllText(path, content.Replace(old, replacement, StringComparison.Ordinal), new UTF8Encoding(false));
        }
        public void CreateZip(string sequence, string variant, string name = "package.zip", CompressionLevel compression = CompressionLevel.Optimal)
        {
            using var archive = ZipFile.Open(ArchivePath(name), ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(PathOf(sequence), "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(PathOf(""), file).Replace('\\', '/');
                if (variant == "missing-path" && relative.EndsWith("/overview.pdf", StringComparison.Ordinal)) continue;
                using var stream = archive.CreateEntry(relative, compression).Open();
                var data = File.ReadAllBytes(file);
                stream.Write(data);
                if (variant == "changed-content" && relative.EndsWith("/overview.pdf", StringComparison.Ordinal)) stream.WriteByte(0);
            }
            if (variant == "empty-directory") archive.CreateEntry(sequence + "/empty/");
            if (variant == "extra-root")
            {
                using var output = archive.CreateEntry("unexpected.txt").Open();
                output.WriteByte(1);
            }
        }
        public async Task<(PackageDeliveryInput Input, PackageLifecycleInspection Lifecycle)> SelectAsync(string sequence, bool comparison = false,
            string archive = "package.zip", string[]? history = null, bool applicationRoot = false, PackageReadLimits? limits = null)
        {
            async Task<ICapturedPackageInput> Capture(string path, string number)
            {
                var capture = await _reader.ReadAsync(new(ApplicationId, "source-" + number + "-" + Path.GetFileName(path), path, number), limits ?? new());
                _captures.Add(capture);
                return capture;
            }
            var target = await Capture(applicationRoot ? PathOf("") : PathOf(sequence), sequence);
            var selected = new List<ICapturedPackageInput>();
            foreach (var number in history ?? HistoricalSequences.Where(number => string.CompareOrdinal(number, sequence) < 0).ToArray())
                selected.Add(await Capture(PathOf(number), number));
            var baseline = new HistoryBaselineManifest(ApplicationId, sequence, selected.Select(capture => new HistoryBaselineEntry(capture.Manifest.SequenceNumber,
                HistorySourceKind.ImportedExternal, capture.SourceId, capture.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)));
            var packages = new PackageInputSet(target, baseline, selected);
            var lifecycle = await new PackageLifecycleInspector().InspectAsync(packages, PackageValidationCatalog.UsProfile);
            return (new(packages, comparison ? await Capture(ArchivePath(archive), sequence) : null), lifecycle);
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var capture in _captures) await capture.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }
}
