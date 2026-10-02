using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageInputReaderTests
{
    private static readonly Guid ApplicationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static string Fixture(string sequence = "") => Path.Combine(PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/sequences", sequence);
    private static PackageInputSelection Select(string path, string? sequence = null, string? root = null, string source = "external") => new(ApplicationId, source, path, sequence, root);

    [Theory]
    [InlineData("0000")]
    [InlineData("0001")]
    [InlineData("0002")]
    [InlineData("0003")]
    public async Task ExternalDirectoryAndZipHaveIdenticalIndependentManifestAndBytes(string sequence)
    {
        using var tree = new TestTree();
        var zip = Path.Combine(tree.Root, "delivery.zip");
        ZipFile.CreateFromDirectory(Fixture(sequence), zip, CompressionLevel.NoCompression, includeBaseDirectory: true);
        var reader = new PackageInputReader(tree.Capture);
        await using var directory = await reader.ReadAsync(Select(Fixture(sequence)), new());
        await using var archive = await reader.ReadAsync(Select(zip), new());
        Assert.Equal(directory.Manifest.Digest, archive.Manifest.Digest);
        Assert.NotEqual(directory.InputDigest, archive.InputDigest);
        Assert.Null(directory.ArchiveSha256);
        Assert.NotNull(archive.ArchiveSha256);
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(PackageRuleCatalogTests.Root, "reference/publisher/input-manifest-v2-vectors.json")));
        Assert.Equal(vectors.RootElement.GetProperty("sequenceDigests").GetProperty(sequence).GetString(), directory.Manifest.Digest);
        using var examples = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "contract-examples.json")));
        var expected = examples.RootElement.GetProperty("packageInputs").EnumerateArray().FirstOrDefault(input => input.GetProperty("content").GetProperty("sequenceNumber").GetString() == sequence);
        if (expected.ValueKind != JsonValueKind.Undefined)
        {
            foreach (var file in expected.GetProperty("content").GetProperty("files").EnumerateArray())
            {
                var actual = Assert.Single(directory.Manifest.Files, entry => entry.LogicalPath == file.GetProperty("logicalPath").GetString());
                Assert.Equal(file.GetProperty("sha256").GetString(), actual.Sha256);
                Assert.Equal(file.GetProperty("md5").GetString(), actual.Md5);
                Assert.Equal(file.GetProperty("length").GetInt64(), actual.Length);
            }
        }
        await using var captured = archive.OpenRead(sequence + "/index.xml");
        using var bytes = new MemoryStream();
        await captured.CopyToAsync(bytes);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(Fixture(sequence), "index.xml")), bytes.ToArray());
        await archive.VerifyUnchangedAsync();
        Assert.Throws<FileNotFoundException>(() => archive.OpenRead(sequence + "/INDEX.XML"));
    }

    [Fact]
    public async Task MultipleCandidatesRequireSelectionAndNeverInferHistory()
    {
        using var tree = new TestTree();
        var reader = new PackageInputReader(tree.Capture);
        var exception = await Assert.ThrowsAsync<PackageSelectionRequiredException>(() => reader.ReadAsync(Select(Fixture()), new()));
        Assert.Equal(4, exception.Candidates.Count);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
        await using var input = await reader.ReadAsync(Select(Fixture(), "0001"), new());
        Assert.Contains(input.Manifest.AdditionalFiles, file => file.LogicalPath == "0000/index.xml");
        Assert.Throws<FileNotFoundException>(() => input.OpenRead("0000/index.xml"));
        var baseline = new HistoryBaselineManifest(ApplicationId, "0001", []);
        var set = new PackageInputSet(input, baseline, []);
        Assert.Equal("HISTORY_NOT_AVAILABLE", Assert.Throws<PackageInputException>(() => set.OpenRead("0000/index.xml")).Code);
    }

    [Fact]
    public async Task ResentCopiesOfTheSameSequenceRequireExactRootSelection()
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("first/0000/index.xml", "first"), ("resent/0000/index.xml", "resent"));
        var reader = new PackageInputReader(tree.Capture);
        var exception = await Assert.ThrowsAsync<PackageSelectionRequiredException>(() => reader.ReadAsync(Select(zip, "0000"), new()));
        Assert.Equal(2, exception.Candidates.Count);
        await using var input = await reader.ReadAsync(Select(zip, "0000", "resent/0000"), new());
        using var content = new StreamReader(input.OpenRead("0000/index.xml"));
        Assert.Equal("resent", await content.ReadToEndAsync());
    }

    [Fact]
    public async Task RootOnlyArchiveRequiresAnExplicitSequenceNumber()
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("index.xml", "root"));
        var reader = new PackageInputReader(tree.Capture);
        await Assert.ThrowsAsync<PackageSelectionRequiredException>(() => reader.ReadAsync(Select(zip), new()));
        await using var input = await reader.ReadAsync(Select(zip, "0012"), new());
        Assert.Equal("0012/index.xml", Assert.Single(input.Manifest.Files).LogicalPath);
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("/escaped.txt")]
    [InlineData("C:/escaped.txt")]
    [InlineData("0000/../escaped.txt")]
    [InlineData("0000\\escaped.txt")]
    [InlineData("0000/CON.pdf")]
    [InlineData("0000/name.")]
    [InlineData("0000/name ")]
    [InlineData("0000//name")]
    [InlineData("0000/name:stream")]
    public async Task UnsafeZipNamesAreRejectedBeforePayloadCapture(string name)
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "root"), (name, "unsafe"));
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture).ReadAsync(Select(zip), new()));
        Assert.Equal("UNSAFE_ZIP_ENTRY", error.Code);
        Assert.Equal(name, error.LogicalPath);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
        Assert.False(File.Exists(Path.Combine(tree.Root, "escaped.txt")));
        Assert.True(File.Exists(zip));
    }

    [Theory]
    [InlineData("0000/index.xml", "0000/index.xml")]
    [InlineData("0000/index.xml", "0000/INDEX.XML")]
    [InlineData("0000/caf\u00e9.pdf", "0000/cafe\u0301.pdf")]
    [InlineData("0000/m1", "0000/m1/file.pdf")]
    [InlineData("0000/m1/", "0000/m1/")]
    public async Task DuplicateOrPortableCollidingZipEntriesAreRejected(string first, string second)
    {
        using var tree = new TestTree();
        var zip = tree.Zip((first, first.EndsWith('/') ? "" : "one"), (second, second.EndsWith('/') ? "" : "two"));
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture).ReadAsync(Select(zip), new()));
        Assert.Equal("DUPLICATE_ZIP_ENTRY", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
    }

    [Theory]
    [InlineData(unchecked((int)0xa1ff0000))]
    [InlineData(0x400)]
    [InlineData(0x10000000)]
    public async Task ZipSymlinkReparseAndSpecialEntriesAreRejected(int attributes)
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "root"));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) archive.Entries[0].ExternalAttributes = attributes;
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture).ReadAsync(Select(zip), new()));
        Assert.Equal("UNSAFE_ZIP_ENTRY", error.Code);
    }

    [Theory]
    [InlineData("file", "FILE_SIZE_LIMIT")]
    [InlineData("total", "TOTAL_SIZE_LIMIT")]
    [InlineData("entries", "ZIP_METADATA_LIMIT")]
    [InlineData("archive", "ARCHIVE_SIZE_LIMIT")]
    [InlineData("metadata", "ZIP_METADATA_LIMIT")]
    [InlineData("ratio", "COMPRESSION_RATIO_LIMIT")]
    public async Task ConfiguredZipResourceLimitsRejectActualInputsAndCleanOnlyOwnedCapture(string dimension, string code)
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", new string('a', 1000)), ("0000/file.pdf", new string('b', 1000)));
        var limits = dimension switch
        {
            "file" => new PackageReadLimits { MaxFileBytes = 500 },
            "total" => new PackageReadLimits { MaxExpandedBytes = 1500 },
            "entries" => new PackageReadLimits { MaxEntries = 1 },
            "archive" => new PackageReadLimits { MaxArchiveBytes = 10 },
            "metadata" => new PackageReadLimits { MaxCentralDirectoryBytes = 10 },
            "ratio" => new PackageReadLimits { MaxCompressionRatio = 1 },
            _ => throw new ArgumentException("Unknown dimension")
        };
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture).ReadAsync(Select(zip), limits));
        Assert.Equal(code, error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task ForgedCentralDirectoryCountsAreRejectedBeforeFrameworkAllocation()
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "one"), ("0000/file.pdf", "two"));
        var bytes = await File.ReadAllBytesAsync(zip);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 10), 1);
        await File.WriteAllBytesAsync(zip, bytes);
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture).ReadAsync(Select(zip), new()));
        Assert.Equal("INVALID_INPUT_DATA", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
    }

    [Fact]
    public async Task Zip64DirectoryMetadataIsReadWithoutUnboundedAllocation()
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "root"));
        var original = await File.ReadAllBytesAsync(zip);
        var endOffset = original.Length - 22;
        var extended = new byte[original.Length + 76];
        original.AsSpan(0, endOffset).CopyTo(extended);
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(endOffset), 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(endOffset + 4), 44);
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(endOffset + 12), 45);
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(endOffset + 14), 45);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(endOffset + 24), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(endOffset + 32), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(endOffset + 40), BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(endOffset + 12)));
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(endOffset + 48), BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(endOffset + 16)));
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(endOffset + 56), 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(endOffset + 64), (ulong)endOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(endOffset + 72), 1);
        original.AsSpan(endOffset).CopyTo(extended.AsSpan(endOffset + 76));
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(endOffset + 84), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(endOffset + 86), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(endOffset + 88), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(endOffset + 92), uint.MaxValue);
        await File.WriteAllBytesAsync(zip, extended);
        await using var input = await new PackageInputReader(tree.Capture).ReadAsync(Select(zip), new());
        Assert.Equal("0000/index.xml", Assert.Single(input.Manifest.Files).LogicalPath);
    }

    [Fact]
    public async Task DirectoryByteLimitsAreAppliedToRealFiles()
    {
        using var tree = new TestTree();
        var source = tree.Sequence();
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture)
            .ReadAsync(Select(source), new() { MaxFileBytes = 2 }));
        Assert.Equal("FILE_SIZE_LIMIT", error.Code);
        Assert.Equal("INPUT-LIMITS", error.RuleId);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
    }

    [Theory]
    [InlineData(10, 64)]
    [InlineData(1024, 1)]
    public async Task ConfiguredLogicalPathLimitsApplyBeforeCapture(int length, int depth)
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "root"));
        var error = await Assert.ThrowsAsync<PackageInputException>(() => new PackageInputReader(tree.Capture)
            .ReadAsync(Select(zip), new() { MaxPathLength = length, MaxPathDepth = depth }));
        Assert.Equal("UNSAFE_ZIP_ENTRY", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
    }

    [Fact]
    public async Task EmptyDirectoriesAndUnselectedTopLevelFilesParticipateInDigest()
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "root"), ("0000/empty/", ""), ("cover.txt", "cover"));
        var reader = new PackageInputReader(tree.Capture);
        await using var input = await reader.ReadAsync(Select(zip), new());
        Assert.Contains(input.Manifest.Directories, directory => directory.LogicalPath == "0000/empty" && directory.IsEmpty);
        Assert.Equal("cover.txt", Assert.Single(input.Manifest.AdditionalFiles).LogicalPath);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) archive.GetEntry("0000/empty/")!.Delete();
        await Assert.ThrowsAsync<PackageInputChangedException>(() => input.VerifyUnchangedAsync());
        await using var changed = await reader.ReadAsync(Select(zip), new());
        Assert.NotEqual(input.Manifest.Digest, changed.Manifest.Digest);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("capture")]
    public async Task ChangedInputOrCapturedBytesInvalidateTheRun(string change)
    {
        using var tree = new TestTree();
        var directory = tree.Sequence();
        var reader = new PackageInputReader(tree.Capture);
        await using var input = await reader.ReadAsync(Select(directory), new());
        switch (change)
        {
            case "bytes": await File.WriteAllTextAsync(Path.Combine(directory, "index.xml"), "changed"); break;
            case "add": await File.WriteAllTextAsync(Path.Combine(directory, "extra.txt"), "added"); break;
            case "delete": File.Delete(Path.Combine(directory, "index.xml")); break;
            case "capture": await File.WriteAllTextAsync(Directory.EnumerateFiles(tree.Capture, "*", SearchOption.AllDirectories).First(), "tampered"); break;
        }
        Assert.Equal("INPUT_CHANGED", (await Assert.ThrowsAsync<PackageInputChangedException>(() => input.VerifyUnchangedAsync())).Code);
        if (change == "bytes")
        {
            using var captured = new StreamReader(input.OpenRead("0000/index.xml"));
            Assert.Equal("original", await captured.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task CancellationDuringCopyPreservesSourceAndRemovesTaskCapture()
    {
        using var tree = new TestTree();
        var source = tree.Sequence();
        await File.WriteAllBytesAsync(Path.Combine(source, "payload.pdf"), new byte[32 * 1024 * 1024]);
        var reader = new PackageInputReader(tree.Capture);
        using var cancellation = new CancellationTokenSource();
        var task = reader.ReadAsync(Select(source), new(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
        Assert.Equal(32 * 1024 * 1024, new FileInfo(Path.Combine(source, "payload.pdf")).Length);
    }

    [Fact]
    public async Task ConfiguredDeadlineStopsCaptureAndIsDistinctFromUserCancellation()
    {
        using var tree = new TestTree();
        var source = tree.Sequence();
        await File.WriteAllBytesAsync(Path.Combine(source, "payload.pdf"), new byte[32 * 1024 * 1024]);
        var time = new TriggerTimeProvider();
        var task = new PackageInputReader(tree.Capture, time).ReadAsync(Select(source), new());
        time.Trigger();
        var error = await Assert.ThrowsAsync<PackageInputException>(() => task);
        Assert.Equal("INPUT_READ_TIMEOUT", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
        Assert.True(File.Exists(Path.Combine(source, "payload.pdf")));
    }

    [Fact]
    public async Task DirectoryAndAncestorLinksCannotReadExternalFiles()
    {
        using var tree = new TestTree();
        var source = tree.Sequence();
        var outside = Path.Combine(tree.Root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "sentinel.txt"), "untouched");
        var link = Path.Combine(source, "linked");
        CreateDirectoryLink(link, outside);
        try
        {
            var reader = new PackageInputReader(tree.Capture);
            await Assert.ThrowsAsync<PackageInputException>(() => reader.ReadAsync(Select(source), new()));
            await Assert.ThrowsAsync<PackageInputException>(() => reader.ReadAsync(Select(link, "0000"), new()));
            Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Capture));
            Assert.Equal("untouched", await File.ReadAllTextAsync(Path.Combine(outside, "sentinel.txt")));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task RepackedArchiveWithIdenticalPayloadCannotReuseExactInputBinding()
    {
        using var tree = new TestTree();
        var zip = tree.Zip(("0000/index.xml", "root"));
        var reader = new PackageInputReader(tree.Capture);
        await using var first = await reader.ReadAsync(Select(zip), new());
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) archive.Entries[0].LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var second = await reader.ReadAsync(Select(zip), new());
        Assert.Equal(first.Manifest.Digest, second.Manifest.Digest);
        Assert.NotEqual(first.InputDigest, second.InputDigest);
        await Assert.ThrowsAsync<PackageInputChangedException>(() => first.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task InputAndCaptureDirectoriesCannotOverlap()
    {
        using var tree = new TestTree();
        var source = tree.Sequence();
        var reader = new PackageInputReader(Path.Combine(source, "capture"));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(Select(source), new()));
    }

    [Fact]
    public async Task ExplicitHistoricalInputsResolveAcrossIndependentPhysicalLocations()
    {
        using var tree = new TestTree();
        var reader = new PackageInputReader(tree.Capture);
        var zip = Path.Combine(tree.Root, "history.zip");
        ZipFile.CreateFromDirectory(Fixture("0000"), zip, CompressionLevel.NoCompression, includeBaseDirectory: true);
        await using var target = await reader.ReadAsync(Select(Fixture("0001"), source: "current"), new());
        await using var history = await reader.ReadAsync(Select(zip, source: "chosen-history"), new());
        var entry = new HistoryBaselineEntry("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest,
            PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified);
        var baseline = new HistoryBaselineManifest(ApplicationId, "0001", [entry]);
        var set = new PackageInputSet(target, baseline, [history]);
        await using var stream = set.OpenRead("0000/m1/us/us-regional.xml");
        Assert.True(stream.Length > 0);
        Assert.Equal(HistoryTrustStatus.Unverified, Assert.Single(set.Baseline.Entries).TrustStatus);
        await set.VerifyUnchangedAsync();
        Assert.Throws<ArgumentException>(() => new PackageInputSet(target, new(ApplicationId, "0001", [entry with { SourceId = "unchosen-copy" }]), [history]));
        Assert.Throws<ArgumentException>(() => new PackageInputSet(target, new(ApplicationId, "0001", [entry with { ContentDigest = new string('0', 64) }]), [history]));
        Assert.Throws<ArgumentException>(() => new PackageInputSet(target, baseline, []));
        Assert.Throws<ArgumentException>(() => new HistoryBaselineManifest(ApplicationId, "0001", [entry, entry]));
        Assert.Throws<ArgumentException>(() => new HistoryBaselineManifest(ApplicationId, "0001", [entry with { SequenceNumber = "0002" }]));
        Assert.Throws<ArgumentException>(() => new PackageInputSet(target, new(Guid.NewGuid(), "0001", [entry]), [history]));
    }

    private static void CreateDirectoryLink(string path, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path, target); return; }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class TestTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ratools-input-test-" + Guid.NewGuid().ToString("N"));
        public string Capture => Path.Combine(Root, "capture");
        public TestTree() => Directory.CreateDirectory(Root);
        public string Sequence()
        {
            var path = Path.Combine(Root, "source", "0000");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "index.xml"), "original");
            return path;
        }
        public string Zip(params (string Path, string Content)[] entries)
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
            return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class TriggerTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new InertTimer();
        }
        public void Trigger() => _callback!(_state);
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
