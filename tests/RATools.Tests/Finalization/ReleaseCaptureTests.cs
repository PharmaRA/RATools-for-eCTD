using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RATools.Application.Finalization;
using RATools.Domain.Finalization;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Finalization;

// P4-02/P4-03 storage and identity contract for captured releases: independent copies,
// measured hashes, deterministic manifests and content-addressed directories.
public sealed class ReleaseCaptureTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-release-capture-" + Guid.NewGuid().ToString("N"));
    private readonly LocalReleaseCaptureStore _store;
    private readonly Guid _application = Guid.NewGuid();

    public ReleaseCaptureTests() =>
        _store = new LocalReleaseCaptureStore(Options.Create(new BackboneOutputOptions { RootPath = _root }));

    [Fact]
    public async Task CaptureCopiesBytesAndPublishesTheManifestIntoAContentDirectory()
    {
        var source = await WriteSourceAsync("source/m2/25-clin-over/overview.pdf", "the overview");
        var generated = Encoding.UTF8.GetBytes("<index />");
        var plan = new ReleaseCapturePlan(_application, "0001",
        [
            ReleaseCaptureFile.Copy("payload/0001/m2/25-clin-over/overview.pdf", source, new FileInfo(source).Length,
                await ShaAsync(source), await Md5Async(source)),
            ReleaseCaptureFile.Generated("payload/0001/index.xml", generated),
            ReleaseCaptureFile.Generated("validation-report.json", Encoding.UTF8.GetBytes("{}"), ReleaseFileKind.ValidationReport)
        ]);

        var staging = await _store.SaveAsync(plan);
        var manifest = Manifest(staging);
        await _store.CompleteAsync(staging, manifest);
        var published = await _store.CommitAsync(staging, manifest.Digest);

        Assert.True(published.Created);
        Assert.Equal(Path.Combine(_root, _application.ToString("N"), "_releases", "0001", manifest.Digest), published.Directory);
        Assert.True(File.Exists(Path.Combine(published.Directory, "manifest.json")));
        // The delivered bytes are an independent copy, not a hard link to the workspace.
        var copied = Path.Combine(published.Directory, "payload", "0001", "m2", "25-clin-over", "overview.pdf");
        await File.AppendAllTextAsync(source, " changed after capture");
        Assert.Equal("the overview", await File.ReadAllTextAsync(copied));
        // The manifest round-trips from disk and reproduces its committed digest.
        var reloaded = await _store.ReadManifestAsync(_application, "0001", manifest.Digest);
        Assert.NotNull(reloaded);
        Assert.Equal(manifest.Digest, reloaded!.Digest);
        Assert.Equal(4, reloaded.Files.Count);
        // The delivery package is built from the captured payload, inside the sequence root
        // and without the manifest or the report, so its own hash never nests the manifest.
        using var package = System.IO.Compression.ZipFile.OpenRead(Path.Combine(published.Directory, "delivery-package.zip"));
        Assert.Equal(["0001/index.xml", "0001/m2/25-clin-over/overview.pdf"],
            package.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray());
        using var overview = package.GetEntry("0001/m2/25-clin-over/overview.pdf")!.Open();
        using var reader = new StreamReader(overview);
        Assert.Equal("the overview", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CapturingIdenticalContentTwiceYieldsOneDirectoryAndTheSecondIsNotCreated()
    {
        var first = await CaptureOnceAsync();
        var second = await CaptureOnceAsync();
        // The digest excludes the workspace revision and tool version, so identical
        // validated content is one identity.
        Assert.Equal(first.ManifestDigest, second.ManifestDigest);
        Assert.True(first.Published);
        Assert.False(second.Published);
        Assert.Equal(first.Directory, second.Directory);
    }

    [Fact]
    public async Task ASourceChangedAfterPlanningIsRefusedInsteadOfCaptured()
    {
        var source = await WriteSourceAsync("source/file.pdf", "original");
        var plan = new ReleaseCapturePlan(_application, "0001",
        [
            ReleaseCaptureFile.Copy("payload/0001/file.pdf", source, 8, await ShaAsync(source), await Md5Async(source)),
            ReleaseCaptureFile.Generated("validation-report.json", Encoding.UTF8.GetBytes("{}"), ReleaseFileKind.ValidationReport)
        ]);
        await File.WriteAllTextAsync(source, "tampered");
        await Assert.ThrowsAsync<ReleaseCaptureSourceChangedException>(() => _store.SaveAsync(plan));
        // Nothing partial is left behind for a later reader to mistake for evidence.
        var stagingRoot = Path.Combine(_root, _application.ToString("N"), "_staging");
        Assert.True(!Directory.Exists(stagingRoot) || !Directory.EnumerateDirectories(stagingRoot).Any());
    }

    [Fact]
    public async Task AManifestEditedAfterCaptureIsRefusedOnEveryRead()
    {
        var (staging, manifest) = await CaptureAsync();
        var directory = (await _store.CommitAsync(staging, manifest.Digest)).Directory;
        var manifestPath = Path.Combine(directory, "manifest.json");
        // The delivered file is untouched; only the manifest describing it is edited. A
        // reader must refuse rather than report a release that no snapshot committed.
        var text = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(manifestPath, text.Replace("\"files\"", "\"filesX\"", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ReadManifestAsync(_application, "0001", manifest.Digest));
    }

    [Fact]
    public async Task APreviewAddressesOnlyVerifiedManifestMembersAndRefusesTamperedBytes()
    {
        var (staging, manifest) = await CaptureAsync();
        var published = await _store.CommitAsync(staging, manifest.Digest);

        // The served path is resolved from the verified manifest, and the recorded digest is
        // re-measured, so the reader receives the exact captured bytes.
        var pdf = await _store.OpenFileAsync(_application, "0001", manifest.Digest, "payload/0001/m2/file.pdf");
        Assert.Equal("payload/0001/m2/file.pdf", pdf.RelativePath);
        Assert.Equal("application/pdf", pdf.MediaType);
        Assert.Equal("stable content", await File.ReadAllTextAsync(pdf.Path));
        Assert.Equal(manifest.Files.Single(file => file.RelativePath == pdf.RelativePath).Sha256, pdf.Sha256);

        var index = await _store.OpenFileAsync(_application, "0001", manifest.Digest, "payload/0001/index.xml");
        Assert.Equal("application/xml", index.MediaType);
        var report = await _store.OpenFileAsync(_application, "0001", manifest.Digest, "validation-report.json");
        Assert.Equal("application/octet-stream", report.MediaType);

        // A file that exists in the release directory but is not a manifest member is not
        // previewable: the manifest, not the directory listing, is the address space.
        await File.WriteAllTextAsync(Path.Combine(published.Directory, "payload", "0001", "extra.txt"), "unlisted");
        await Assert.ThrowsAsync<ReleaseFileNotFoundException>(() =>
            _store.OpenFileAsync(_application, "0001", manifest.Digest, "payload/0001/extra.txt"));
        // Paths outside the manifest grammar are rejected before any disk access.
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _store.OpenFileAsync(_application, "0001", manifest.Digest, "../manifest.json"));
        // A release directory that does not exist is missing evidence, not an empty preview.
        await Assert.ThrowsAsync<ReleaseCaptureMissingException>(() =>
            _store.OpenFileAsync(_application, "0001", new string('0', 64), "payload/0001/m2/file.pdf"));

        // Bytes edited after capture no longer reproduce the recorded digest; the preview
        // fails instead of serving content the snapshot never committed.
        await File.AppendAllTextAsync(Path.Combine(published.Directory, "payload", "0001", "m2", "file.pdf"), " tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _store.OpenFileAsync(_application, "0001", manifest.Digest, "payload/0001/m2/file.pdf"));
    }

    [Fact]
    public void TheBaselineMustBeTheArchivedSelectionTheRunBound()
    {
        var baseline = JsonSerializer.SerializeToElement(new
        {
            applicationId = _application.ToString("D"), targetSequence = "0001", digest = new string('a', 64),
            entries = new[]
            {
                new
                {
                    sequenceNumber = "0000", sourceKind = "ImportedExternal", sourceId = "input-0",
                    contentDigest = new string('b', 64), profileSnapshotId = "profile", trustStatus = "Unverified",
                    validationRunId = (string?)null, finalizedSequenceId = (string?)null
                }
            }
        });
        var files = new[]
        {
            new ReleaseManifestFile("payload/0001/index.xml", 7, new string('c', 64), new string('d', 32), ReleaseFileKind.Payload),
            new ReleaseManifestFile("validation-report.json", 2, new string('e', 64), new string('f', 32), ReleaseFileKind.ValidationReport)
        };
        var validation = new ReleaseValidationReference(new string('e', 64), "Development", "profile", "3.2.2", null,
            new string('a', 64), "engine", null);
        // The recorded baseline digest must be the archived manifest's own digest.
        Assert.Throws<ArgumentException>(() => new ReleaseManifest(_application, "0001", 1, "tool", new string('b', 64),
            baseline, new string('9', 64), validation, [], files, [], [], []));
        // A baseline for another sequence cannot describe this release.
        var otherSequence = JsonSerializer.SerializeToElement(new
        {
            applicationId = _application.ToString("D"), targetSequence = "0002", digest = new string('a', 64),
            entries = Array.Empty<object>()
        });
        Assert.Throws<ArgumentException>(() => new ReleaseManifest(_application, "0001", 1, "tool", new string('b', 64),
            otherSequence, new string('a', 64), validation, [], files, [], [], []));
        // The selected historical entry is carried into the manifest and round-trips.
        var manifest = new ReleaseManifest(_application, "0001", 1, "tool", new string('b', 64), baseline,
            new string('a', 64), validation, [], files, [], [], []);
        var reference = Assert.Single(manifest.HistoryReferences);
        Assert.Equal("0000", reference.SequenceNumber);
        Assert.Equal(new string('b', 64), reference.ContentDigest);
        var roundTripped = ReleaseManifest.FromJson(manifest.ToUtf8Json(), _application, "0001", manifest.Digest);
        Assert.Equal(manifest.Digest, roundTripped.Digest);
        Assert.Equal(reference, Assert.Single(roundTripped.HistoryReferences));
    }

    private async Task<(ReleaseCaptureStaging Staging, ReleaseManifest Manifest)> CaptureAsync()
    {
        var source = await WriteSourceAsync("source/" + Guid.NewGuid().ToString("N") + ".pdf", "stable content");
        var plan = new ReleaseCapturePlan(_application, "0001",
        [
            ReleaseCaptureFile.Copy("payload/0001/m2/file.pdf", source, new FileInfo(source).Length,
                await ShaAsync(source), await Md5Async(source)),
            ReleaseCaptureFile.Generated("payload/0001/index.xml", Encoding.UTF8.GetBytes("<index />")),
            ReleaseCaptureFile.Generated("validation-report.json", Encoding.UTF8.GetBytes("{}"), ReleaseFileKind.ValidationReport)
        ]);
        var staging = await _store.SaveAsync(plan);
        var manifest = Manifest(staging);
        await _store.CompleteAsync(staging, manifest);
        return (staging, manifest);
    }

    // Captures the same bytes from a fresh copy, so only the content identity is shared.
    private async Task<(string ManifestDigest, bool Published, string Directory)> CaptureOnceAsync()
    {
        var (staging, manifest) = await CaptureAsync();
        var result = await _store.CommitAsync(staging, manifest.Digest);
        return (manifest.Digest, result.Created, result.Directory);
    }

    private ReleaseManifest Manifest(ReleaseCaptureStaging staging) =>
        new(_application, "0001", 7, FinalizationService.ToolVersion, new string('b', 64), EmptyBaseline(),
            new string('a', 64),
            new ReleaseValidationReference(new string('e', 64), "Development", "profile", "3.2.2", null,
                new string('a', 64), "engine", null), [], staging.Files, [], [], []);

    private JsonElement EmptyBaseline() => JsonSerializer.SerializeToElement(new
    {
        applicationId = _application.ToString("D"), targetSequence = "0001", digest = new string('a', 64), entries = Array.Empty<object>()
    });

    private async Task<string> WriteSourceAsync(string relative, string content)
    {
        var path = Path.Combine(_root, "workspace", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        return path;
    }

    private static async Task<string> ShaAsync(string path) =>
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();

    private static async Task<string> Md5Async(string path) =>
        Convert.ToHexString(MD5.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root) && OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.GetFullPath(_root), new DirectoryInfo(_root).FullName);
            Assert.StartsWith(Path.GetTempPath(), Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(_root);
            while (pending.TryPop(out var current))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    Assert.Equal(0, (int)(File.GetAttributes(entry) & FileAttributes.ReparsePoint));
                    if (Directory.Exists(entry)) pending.Push(entry);
                }
            }
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(_root, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        return ValueTask.CompletedTask;
    }
}
