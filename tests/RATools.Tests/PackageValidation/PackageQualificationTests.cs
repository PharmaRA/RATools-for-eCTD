using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

// P2-09 qualification matrix: every deliberate defect in the frozen fixture must be
// detected and located by the independent engine, without importing workspace data.
// Cases come from invalid-cases.json; the expected code is part of the reviewed fixture.
public sealed class PackageQualificationTests
{
    private static readonly Guid ApplicationId = Guid.Parse("00000000-0000-0000-0000-000000000009");
    // Qualification rules have no per-run execution path by design; a report must
    // therefore state that explicitly rather than silently omit them.
    private static readonly string[] QualificationOnlyRules = ["EXTERNAL-COMPARISON", "US-CRITERIA-INVENTORY", "EU-CRITERIA-INVENTORY"];

    public static TheoryData<string, string> DefectCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var item in ReadCases()) data.Add(item.Id, item.Expected);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DefectCases))]
    public async Task DeliberateDefectIsDetectedByTheIndependentEngine(string id, string expectedCode)
    {
        using var fixture = new QualificationFixture();
        var caseData = ReadCases().Single(item => item.Id == id);
        fixture.Apply(caseData);
        PackageDeliveryInspection result;
        try { result = await fixture.InspectAsync(caseData); }
        catch (PackageInputException rejection)
        {
            // Unsafe ZIP namespaces are rejected during controlled capture; that is the
            // qualified outcome for traversal/duplicate-entry cases, before any payload
            // is written or any rule executes.
            Assert.Equal(expectedCode, rejection.Code);
            Assert.NotNull(rejection.LogicalPath);
            return;
        }
        var codes = result.Checks.SelectMany(check => check.Findings).Select(finding => finding.Code).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(expectedCode, codes);
        var report = result.CreateReport(PackageValidationMode.Formal);
        Assert.False(report.IsReadyForFinalization);
        var finding = report.Coverage.SelectMany(rule => rule.Findings).First(item => item.Code == expectedCode);
        Assert.NotNull(finding.Location);
    }

    [Fact]
    public async Task BaselineSequencesProduceCompleteLocatedCoverage()
    {
        foreach (var sequence in new[] { "0000", "0001", "0002", "0003" })
        {
            using var fixture = new QualificationFixture();
            var (input, baseline) = await fixture.CaptureAsync(sequence);
            var result = await new PackageDeliveryInspector(new PdfPigPackageInspector())
                .InspectAsync(input, PackageValidationCatalog.UsProfile);
            Assert.All(result.Checks.SelectMany(check => check.Findings),
                finding => Assert.NotEqual(CheckStatus.Fail, finding.CheckStatus));
            var report = result.CreateReport(PackageValidationMode.Formal);
            // Any rule without an execution-level observation must say why. Rules that
            // the implementation cannot fully close report NotEvaluated with an
            // implementation note; ZIP-only rules without a ZIP report NotApplicable.
            var unobserved = report.Coverage.Where(rule => rule.Reason == "The selected rule was not executed.").ToArray();
            Assert.All(unobserved, rule => Assert.Contains(rule.RuleId, QualificationOnlyRules, StringComparer.Ordinal));
            Assert.Equal(input.InputDigest, report.Binding.InputManifestDigest);
            Assert.Equal(baseline.Digest, report.Binding.HistoryBaselineDigest);
            Assert.All(report.Coverage, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Reason)));
        }
    }

    private static List<Case> ReadCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/invalid-cases.json")));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => new Case(
            item.GetProperty("id").GetString()!,
            item.GetProperty("expectedDefect").GetString()!,
            item.GetProperty("targetSequence").GetString()!,
            item.TryGetProperty("mutation", out var mutation) ? mutation.Clone() : null,
            item.TryGetProperty("zipMutation", out var zip) ? zip.Clone() : null,
            item.TryGetProperty("omitHistory", out var omit) ? omit.EnumerateArray().Select(value => value.GetString()!).ToArray() : [],
            item.TryGetProperty("cloneSequence", out var clone) ? clone.Clone() : null)).ToList();
    }

    internal sealed record Case(string Id, string Expected, string TargetSequence, JsonElement? Mutation,
        JsonElement? ZipMutation, string[] OmitHistory, JsonElement? CloneSequence);

    private sealed class QualificationFixture : IDisposable
    {
        private static readonly string[] AllSequences = ["0000", "0001", "0002", "0003"];
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-qualification-" + Guid.NewGuid().ToString("N"));
        private readonly List<ICapturedPackageInput> _captures = [];
        private readonly PackageInputReader _reader;

        public QualificationFixture()
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

        public string PathOf(string relative) => Path.Combine(_root, "source", relative.Replace('/', Path.DirectorySeparatorChar));

        public void Apply(Case data)
        {
            if (data.CloneSequence is { } clone)
            {
                var from = clone.GetProperty("source").GetString()!;
                var to = clone.GetProperty("destination").GetString()!;
                CopyDirectory(PathOf(from), PathOf(to));
                foreach (var xml in Directory.EnumerateFiles(PathOf(to), "*", SearchOption.AllDirectories))
                {
                    var text = File.ReadAllText(xml);
                    File.WriteAllText(xml, text.Replace(from, to, StringComparison.Ordinal), new UTF8Encoding(false));
                }
            }
            if (data.Mutation is { } mutation)
            {
                var path = PathOf(mutation.GetProperty("path").GetString()!);
                if (mutation.GetProperty("kind").GetString() == "text")
                {
                    var text = File.ReadAllText(path);
                    var old = mutation.GetProperty("old").GetString()!;
                    Assert.Equal(1, CountOccurrences(text, old));
                    File.WriteAllText(path, text.Replace(old, mutation.GetProperty("new").GetString()!, StringComparison.Ordinal), new UTF8Encoding(false));
                }
                else File.AppendAllText(path, mutation.GetProperty("text").GetString());
            }
            RefreshIndexChecksums();
            // Build the complete delivery ZIP first, then apply the reviewed ZIP defect.
            // A defect-only archive would fail earlier as a missing sequence root and
            // would not qualify the intended check.
            using var archive = ZipFile.Open(ArchivePath(data.TargetSequence), ZipArchiveMode.Create);
            var replacePath = data.ZipMutation is { } replace && replace.GetProperty("kind").GetString() == "replace"
                ? replace.GetProperty("path").GetString() : null;
            foreach (var file in Directory.EnumerateFiles(PathOf(data.TargetSequence), "*", SearchOption.AllDirectories))
            {
                var name = Path.GetRelativePath(PathOf(""), file).Replace('\\', '/');
                var bytes = name == replacePath && data.ZipMutation is { } replacement
                    ? File.ReadAllBytes(PathOf(replacement.GetProperty("source").GetString()!))
                    : File.ReadAllBytes(file);
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(bytes);
            }
            if (data.ZipMutation is { } added && added.GetProperty("kind").GetString() == "add")
            {
                using var stream = archive.CreateEntry(added.GetProperty("path").GetString()!).Open();
                stream.Write(Encoding.UTF8.GetBytes(added.GetProperty("text").GetString()!));
            }
        }

        public async Task<PackageDeliveryInspection> InspectAsync(Case data)
        {
            // A ZIP-entry defect is observed only when the ZIP itself is the selected
            // source; parity cases select the directory and an explicit ZIP companion.
            var zipTarget = data.ZipMutation is { } zip && zip.GetProperty("kind").GetString() == "add";
            var comparison = data.ZipMutation is { } other && other.GetProperty("kind").GetString() == "replace";
            var (input, _) = await CaptureAsync(data.TargetSequence, zipTarget, comparison, data.OmitHistory);
            return await new PackageDeliveryInspector(new PdfPigPackageInspector())
                .InspectAsync(input, PackageValidationCatalog.UsProfile);
        }

        public async Task<(PackageDeliveryInput Input, HistoryBaselineManifest Baseline)> CaptureAsync(string sequence,
            bool zipTarget = false, bool comparison = false, string[]? omitHistory = null)
        {
            async Task<ICapturedPackageInput> Capture(string path, string number)
            {
                var capture = await _reader.ReadAsync(new(ApplicationId, "qualification-" + number + "-" + Path.GetFileName(path), path, number), new());
                _captures.Add(capture);
                return capture;
            }
            var target = await Capture(zipTarget ? ArchivePath(sequence) : PathOf(sequence), sequence);
            var history = new List<ICapturedPackageInput>();
            foreach (var number in AllSequences.Where(number => string.CompareOrdinal(number, sequence) < 0 &&
                         !(omitHistory ?? []).Contains(number, StringComparer.Ordinal)))
                history.Add(await Capture(PathOf(number), number));
            var baseline = new HistoryBaselineManifest(ApplicationId, sequence, history.Select(capture =>
                new HistoryBaselineEntry(capture.Manifest.SequenceNumber, HistorySourceKind.ImportedExternal,
                    capture.SourceId, capture.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)));
            var packages = new PackageInputSet(target, baseline, history);
            return (new(packages, comparison ? await Capture(ArchivePath(sequence), sequence) : null), baseline);
        }

        public string ArchivePath(string sequence) => Path.Combine(_root, "source", "package-" + sequence + ".zip");

        public void Dispose()
        {
            foreach (var capture in _captures) capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private void RefreshIndexChecksums()
        {
            // Mirrors scripts/fixtures/materialize_publisher_fixture.py: a mutated
            // backbone updates its own index-md5, while payload corruption stays visible.
            foreach (var sequence in Directory.EnumerateDirectories(PathOf("")))
            {
                var index = Path.Combine(sequence, "index.xml");
                if (!File.Exists(index)) continue;
                File.WriteAllText(Path.Combine(sequence, "index-md5.txt"),
                    Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(index))).ToLowerInvariant(),
                    new UTF8Encoding(false));
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
                 index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
            return count;
        }
    }
}
