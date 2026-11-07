using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

// All fixtures are retained; disposal must not erase failed-upload evidence.
public sealed class PackageUploadRetentionTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-upload-retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AmbiguousZip_RetainsOriginalBytesAndSelectionCandidates()
    {
        var bytes = Archive("0000", "0001");
        using var content = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<PackageSelectionRequiredException>(() => Registry().UploadAsync(Guid.NewGuid(), content, null, null));
        Assert.Collection(error.Candidates,
            candidate => { Assert.Equal("0000", candidate.SequenceNumber); Assert.Equal("bundle/0000", candidate.RootRelativePath); },
            candidate => { Assert.Equal("0001", candidate.SequenceNumber); Assert.Equal("bundle/0001", candidate.RootRelativePath); });
        Assert.Equal(bytes, await File.ReadAllBytesAsync(RecycledFile()));
        Assert.Empty(OwnedDirectories());
    }

    [Fact]
    public async Task InvalidZip_RetainsBytesAndInputError()
    {
        byte[] bytes = [1, 2, 3];
        using var content = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<PackageInputException>(() => Registry().UploadAsync(Guid.NewGuid(), content, null, null));
        Assert.Equal("INVALID_INPUT_DATA", error.Code);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(RecycledFile()));
    }

    [Fact]
    public async Task ArchiveLimit_RetainsWrittenPrefix()
    {
        var bytes = Enumerable.Repeat((byte)42, 65537).ToArray();
        using var content = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<PackageInputException>(() => Registry(maximumBytes: 65536).UploadAsync(Guid.NewGuid(), content, null, null));
        Assert.Equal("UPLOAD_LIMIT", error.Code);
        Assert.Equal(bytes[..65536], await File.ReadAllBytesAsync(RecycledFile()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedCopy_RetainsPrefixAndOriginalException(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        Exception expected = cancelled ? new OperationCanceledException(cancellation.Token) : new IOException("Controlled input read failure");
        using var content = new InterruptedStream(expected, cancellation, cancelled);
        var actual = await Record.ExceptionAsync(() => Registry().UploadAsync(Guid.NewGuid(), content, null, null, cancellation.Token));
        Assert.Same(expected, actual);
        Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(RecycledFile()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsafeRecycle_RetainsOriginalPathAndErrorWithDiagnostic(bool unexpectedMember)
    {
        var original = new PackageSelectionRequiredException([new("0000", "bundle/0000"), new("0001", "bundle/0001")]);
        var reader = new FailingReader(original, selection =>
        {
            var obstruction = unexpectedMember ? Path.Combine(Path.GetDirectoryName(selection.Path)!, "foreign.txt") : Path.Combine(_root, "inputs", "_recycle");
            File.WriteAllText(obstruction, "retain this other owner's bytes");
        });
        byte[] bytes = [7, 8, 9];
        using var content = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<PackageSelectionRequiredException>(() => Registry(reader: reader).UploadAsync(Guid.NewGuid(), content, null, null));
        Assert.Same(original, error);
        Assert.IsType<IOException>(error.Data["UploadRetentionFailure"]);
        var directory = Assert.Single(OwnedDirectories());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory, "package.zip")));
        var retained = unexpectedMember ? Path.Combine(directory, "foreign.txt") : Path.Combine(_root, "inputs", "_recycle");
        Assert.Equal("retain this other owner's bytes", await File.ReadAllTextAsync(retained));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistrationFailure_KeepsOriginalPathWhenCommitIsUncertain(bool committed)
    {
        var store = DispatchProxy.Create<IPackageValidationStore, RegistrationFailureProxy>();
        var proxy = (RegistrationFailureProxy)store;
        proxy.CommitBeforeFailure = committed;
        var bytes = Archive("0001");
        using var content = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<IOException>(() => Registry(store: store).UploadAsync(Guid.NewGuid(), content, "0001", "bundle/0001"));
        Assert.Same(proxy.Failure, error);
        var input = Assert.IsType<RegisteredPackageInput>(proxy.Input);
        Assert.Equal(committed, await proxy.Inner.GetInputAsync(input.Id) is not null);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(input.SourcePath));
        Assert.Single(OwnedDirectories());
        Assert.False(Path.Exists(Path.Combine(_root, "inputs", "_recycle")));
    }

    [Fact]
    public async Task FailedDiagnosticLogging_DoesNotReplaceOriginalUploadError()
    {
        var original = new IOException("Original upload failure");
        var logger = new FailingLogger();
        var reader = new FailingReader(original, _ => File.WriteAllText(Path.Combine(_root, "inputs", "_recycle"), "obstruction"));
        using var content = new MemoryStream(new byte[] { 10, 11, 12 });
        var actual = await Record.ExceptionAsync(() => Registry(reader: reader, logger: logger).UploadAsync(Guid.NewGuid(), content, null, null));
        Assert.Same(original, actual);
        Assert.IsType<IOException>(original.Data["UploadRetentionFailure"]);
        Assert.Same(logger.Failure, original.Data["UploadRetentionLoggingFailure"]);
        Assert.Equal(new byte[] { 10, 11, 12 }, await File.ReadAllBytesAsync(Path.Combine(Assert.Single(OwnedDirectories()), "package.zip")));
    }

    [Fact]
    public async Task SuccessfulUpload_RemainsReadableAtRegisteredPath()
    {
        var store = new InMemoryPackageValidationStore();
        var registry = Registry(store: store);
        var bytes = Archive("0001");
        using var content = new MemoryStream(bytes);
        var result = await registry.UploadAsync(Guid.NewGuid(), content, "0001", "bundle/0001");
        var registered = Assert.IsType<RegisteredPackageInput>(await store.GetInputAsync(result.Id));
        Assert.True(registered.IsOwnedUpload);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(registered.SourcePath));
        Assert.Equal(result, await registry.GetAsync(result.Id));
        Assert.Single(OwnedDirectories());
        Assert.False(Path.Exists(Path.Combine(_root, "inputs", "_recycle")));
    }

    private PackageValidationInputRegistry Registry(IPackageValidationStore? store = null, IPackageInputReader? reader = null, long maximumBytes = 1024 * 1024,
        ILogger<PackageValidationInputRegistry>? logger = null)
    {
        Directory.CreateDirectory(_root);
        return new(store ?? new InMemoryPackageValidationStore(), reader ?? new PackageInputReader(Path.Combine(_root, "captures")),
            new UploadOnlyPolicy(), Options.Create(new PackageValidationOptions { StorageRoot = _root, ReadLimits = new() { MaxArchiveBytes = maximumBytes } }), logger: logger);
    }

    private string[] OwnedDirectories() => Directory.GetDirectories(Path.Combine(_root, "inputs"))
        .Where(path => Path.GetFileName(path) != "_recycle").ToArray();

    private string RecycledFile() => Path.Combine(Assert.Single(Directory.GetDirectories(Path.Combine(_root, "inputs", "_recycle"))), "package.zip");

    private static byte[] Archive(params string[] sequences)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var sequence in sequences)
            {
                using var writer = new StreamWriter(archive.CreateEntry("bundle/" + sequence + "/index.xml").Open());
                writer.Write("<ectd/>");
            }
        return stream.ToArray();
    }

    private sealed class UploadOnlyPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];
        public string EnsureAllowed(string path) => throw new InvalidOperationException("Owned uploads must not use a caller-selected source path.");
    }

    private sealed class FailingReader(Exception failure, Action<PackageInputSelection> beforeFailure) : IPackageInputReader
    {
        public Task<ICapturedPackageInput> ReadAsync(PackageInputSelection selection, PackageReadLimits limits, CancellationToken cancellationToken = default)
        {
            beforeFailure(selection);
            return Task.FromException<ICapturedPackageInput>(failure);
        }
    }

    private sealed class FailingLogger : ILogger<PackageValidationInputRegistry>
    {
        public InvalidOperationException Failure { get; } = new("Controlled logger failure");
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => throw Failure;
    }

    private sealed class InterruptedStream(Exception failure, CancellationTokenSource cancellation, bool cancelled) : MemoryStream(new byte[] { 4, 5, 6 })
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == Length)
            {
                if (cancelled) cancellation.Cancel();
                return ValueTask.FromException<int>(failure);
            }
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    // Only the persistence boundary is faulted; capture and registry execute normally.
    public class RegistrationFailureProxy : DispatchProxy
    {
        public InMemoryPackageValidationStore Inner { get; } = new();
        public IOException Failure { get; } = new("Controlled registration response failure");
        public bool CommitBeforeFailure { get; set; }
        public RegisteredPackageInput? Input { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IPackageValidationStore.RegisterInputAsync))
                throw new InvalidOperationException("Unexpected persistence operation");
            Input = (RegisteredPackageInput)args![0]!;
            return RegisterAsync(Input);
        }
        private async Task RegisterAsync(RegisteredPackageInput input)
        {
            if (CommitBeforeFailure) await Inner.RegisterInputAsync(input);
            throw Failure;
        }
    }
}
