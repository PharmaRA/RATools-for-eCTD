using System.IO.Compression;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageInputPreflightTests
{
    [Theory]
    [InlineData("empty-directory", PackageInputRole.Directory)]
    [InlineData("single", PackageInputRole.SingleFile)]
    public async Task ChangesBetweenScansCannotProduceEvidence(string scenario, PackageInputRole role)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = CreateInput(root, scenario);
            var clock = new ObservationClock(() => File.WriteAllText(role == PackageInputRole.Directory ? Path.Combine(source, "added.txt") : source, "changed"));
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"), clock);
            await Assert.ThrowsAsync<PackageInputChangedException>(() => reader.InspectShapeAsync(new(Guid.NewGuid(), "change", source), role, new()));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterFirstScanNeverCommitsEmptyEvidence(bool internalTimeout)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var clock = new ObservationClock(cancellation.Cancel, internalTimeout);
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"), clock);
            var source = CreateInput(root, "empty-directory");
            if (internalTimeout)
            {
                var error = await Assert.ThrowsAsync<PackageInputException>(() => reader.InspectShapeAsync(new(Guid.NewGuid(), "timeout", source), PackageInputRole.Directory, new()));
                Assert.Equal("INPUT_READ_TIMEOUT", error.Code);
            }
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.InspectShapeAsync(new(Guid.NewGuid(), "cancel", source), PackageInputRole.Directory, new(), cancellation.Token));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    private sealed class ObservationClock(Action observed, bool expire = false) : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override DateTimeOffset GetUtcNow() { if (expire) _callback!(_state); else observed(); return DateTimeOffset.UtcNow; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; return new InertTimer(); }
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData("single", PackageInputRole.SingleFile, 1)]
    [InlineData("zero-byte", PackageInputRole.SingleFile, 1)]
    [InlineData("one-file-directory", PackageInputRole.Directory, 1)]
    [InlineData("empty-directory", PackageInputRole.Directory, 0)]
    [InlineData("only-directories", PackageInputRole.Directory, 0)]
    [InlineData("one-file-zip", PackageInputRole.ZipContainer, 1)]
    [InlineData("empty-zip", PackageInputRole.ZipContainer, 0)]
    [InlineData("directory-zip", PackageInputRole.ZipContainer, 0)]
    public async Task CapturesShapeWithoutRequiringASequenceRoot(string scenario, PackageInputRole role, int count)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = CreateInput(root, scenario);
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"));
            var selection = new PackageInputSelection(Guid.NewGuid(), "preflight", source);
            var evidence = await reader.InspectShapeAsync(selection, role, new());
            Assert.Equal(count, evidence.Files.Count);
            Assert.Equal(role, evidence.Role);
            Assert.True(evidence.ScanComplete);
            Assert.True(evidence.StabilityVerified);
            Assert.Equal(64, evidence.InputDigest.Length);
            var repeated = await reader.InspectShapeAsync(selection, role, new());
            Assert.Equal(evidence.InputDigest, repeated.InputDigest);
            if (scenario is "only-directories" or "directory-zip") Assert.Contains("empty", evidence.Directories);
            if (role == PackageInputRole.ZipContainer) Assert.NotNull(evidence.ArchiveSha256);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Theory]
    [InlineData("invalid-zip", "INVALID_INPUT_DATA")]
    [InlineData("unsafe-zip", "UNSAFE_ZIP_ENTRY")]
    public async Task UnsafeOrIncompleteContainersCannotBecomeEmptyEvidence(string scenario, string code)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"));
            var error = await Assert.ThrowsAsync<PackageInputException>(() => reader.InspectShapeAsync(
                new(Guid.NewGuid(), "preflight", CreateInput(root, scenario)), PackageInputRole.ZipContainer, new()));
            Assert.Equal(code, error.Code);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public async Task ExplicitRoleAndRootCannotBeSilentlyReinterpreted()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"));
            var source = CreateInput(root, "single");
            await Assert.ThrowsAsync<ArgumentException>(() => reader.InspectShapeAsync(new(Guid.NewGuid(), "preflight", source), PackageInputRole.Directory, new()));
            await Assert.ThrowsAsync<ArgumentException>(() => reader.InspectShapeAsync(new(Guid.NewGuid(), "preflight", source, RootRelativePath: "nested"), PackageInputRole.SingleFile, new()));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Theory]
    [InlineData("empty-directory", PackageInputRole.Directory)]
    [InlineData("empty-zip", PackageInputRole.ZipContainer)]
    public async Task EmptyInputsStillObserveCallerCancellation(string scenario, PackageInputRole role)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.InspectShapeAsync(
                new(Guid.NewGuid(), "preflight", CreateInput(root, scenario)), role, new(), cancellation.Token));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public async Task FileLimitDoesNotBecomeEmptySubmission()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"));
            var error = await Assert.ThrowsAsync<PackageInputException>(() => reader.InspectShapeAsync(
                new(Guid.NewGuid(), "preflight", CreateInput(root, "single")), PackageInputRole.SingleFile, new() { MaxFileBytes = 1 }));
            Assert.Equal("FILE_SIZE_LIMIT", error.Code);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    internal static string CreateInput(string root, string scenario)
    {
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        if (scenario is "single" or "zero-byte")
        {
            var file = Path.Combine(source, "document.pdf"); File.WriteAllText(file, scenario == "zero-byte" ? "" : "%PDF-content"); return file;
        }
        if (scenario is "empty-directory" or "only-directories" or "one-file-directory")
        {
            if (scenario == "only-directories") Directory.CreateDirectory(Path.Combine(source, "empty/nested"));
            if (scenario == "one-file-directory") File.WriteAllText(Path.Combine(source, "document.pdf"), "content");
            return source;
        }
        var zip = Path.Combine(source, "package.zip");
        if (scenario == "invalid-zip") { File.WriteAllText(zip, "broken"); return zip; }
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            if (scenario == "directory-zip") archive.CreateEntry("empty/");
            if (scenario is "one-file-zip" or "unsafe-zip")
            {
                using var stream = archive.CreateEntry(scenario == "unsafe-zip" ? "../document.txt" : "document.txt").Open();
                stream.Write("content"u8);
            }
        }
        return zip;
    }
}
