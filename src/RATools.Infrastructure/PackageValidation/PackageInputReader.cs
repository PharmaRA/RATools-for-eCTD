using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

public sealed class PackageInputReader : IPackageInputReader
{
    private readonly string _captureParent;
    private readonly TimeProvider _timeProvider;

    public PackageInputReader(string captureParent, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _captureParent = Path.GetFullPath(captureParent);
        var ancestor = _captureParent;
        while (!Directory.Exists(ancestor)) ancestor = Path.GetDirectoryName(ancestor) ?? throw new DirectoryNotFoundException("No capture storage ancestor exists.");
        EnsureNoLinks(ancestor);
        Directory.CreateDirectory(_captureParent);
        EnsureNoLinks(_captureParent);
    }

    public async Task<ICapturedPackageInput> ReadAsync(PackageInputSelection selection, PackageReadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.SourceId);
        if (selection.ApplicationId == Guid.Empty) throw new ArgumentException("Select an explicit application identity.", nameof(selection));
        if (selection.SequenceNumber is not null) PackageInputManifest.RequireSequenceNumber(selection.SequenceNumber);
        if (selection.RootRelativePath is { Length: > 0 }) PackageLogicalPath.Validate(selection.RootRelativePath);
        var source = Path.GetFullPath(selection.Path);
        EnsureNoLinks(source);
        if (IsWithin(_captureParent, source) || IsWithin(source, _captureParent))
            throw new ArgumentException("Input and task capture storage must not overlap.", nameof(selection));
        selection = selection with { Path = source };
        var kind = Directory.Exists(source) ? PackageSourceKind.Directory : PackageSourceKind.Zip;
        EnsureNoLinks(_captureParent);
        var owned = Path.Combine(_captureParent, "input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limits.MaxElapsedSeconds), _timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var inventory = await ScanAsync(source, kind, limits, owned, deadline.Token).ConfigureAwait(false);
            var candidate = SelectCandidate(selection, inventory);
            var manifest = CreateManifest(selection.ApplicationId, candidate, inventory);
            var captured = new CapturedInput(selection, kind, candidate, manifest, inventory, limits, owned, _timeProvider);
            await captured.VerifyUnchangedCoreAsync(deadline.Token).ConfigureAwait(false);
            return captured;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DeleteOwnedCapture(owned);
            throw new PackageInputException("INPUT-LIMITS", "INPUT_READ_TIMEOUT", "Package capture exceeded the configured elapsed-time limit.");
        }
        catch (InvalidDataException exception)
        {
            DeleteOwnedCapture(owned);
            throw new PackageInputException(kind == PackageSourceKind.Zip ? "ZIP-PATHS" : "INPUT-STABLE", "INVALID_INPUT_DATA", exception.Message, innerException: exception);
        }
        catch
        {
            DeleteOwnedCapture(owned);
            throw;
        }
    }

    private static PackageSequenceCandidate SelectCandidate(PackageInputSelection selection, Inventory inventory)
    {
        var candidates = new List<PackageSequenceCandidate>();
        foreach (var file in inventory.Files.Keys.Where(path => path == "index.xml" || path.EndsWith("/index.xml", StringComparison.Ordinal)))
        {
            var root = file == "index.xml" ? "" : file[..^10];
            var name = root.Length == 0 ? new DirectoryInfo(selection.Path).Name : root.Split('/')[^1];
            var number = name.Length == 4 && name.All(character => character is >= '0' and <= '9') ? name : null;
            if (number is null && root.Length == 0) number = selection.SequenceNumber;
            if (number is not null) candidates.Add(new(number, root));
        }
        var matches = candidates.Where(candidate => (selection.SequenceNumber is null || candidate.SequenceNumber == selection.SequenceNumber) &&
            (selection.RootRelativePath is null || candidate.RootRelativePath == selection.RootRelativePath)).ToArray();
        if (matches.Length != 1)
        {
            if (candidates.Count > 0 || inventory.Files.ContainsKey("index.xml"))
                throw new PackageSelectionRequiredException(candidates.OrderBy(candidate => candidate.RootRelativePath, StringComparer.Ordinal).ToArray());
            throw new PackageInputException("INPUT-LAYOUT", "SEQUENCE_ROOT_NOT_FOUND", "No sequence root with an exact index.xml was found.");
        }
        return matches[0];
    }

    private static PackageInputManifest CreateManifest(Guid applicationId, PackageSequenceCandidate candidate, Inventory inventory)
    {
        var prefix = candidate.RootRelativePath.Length == 0 ? "" : candidate.RootRelativePath + "/";
        bool Selected(string path) => path.StartsWith(prefix, StringComparison.Ordinal);
        string Logical(string path) => candidate.SequenceNumber + "/" + path[prefix.Length..];
        var files = inventory.Files.Where(pair => Selected(pair.Key)).Select(pair => pair.Value with { LogicalPath = Logical(pair.Key) });
        var extraFiles = inventory.Files.Where(pair => !Selected(pair.Key)).Select(pair => pair.Value);
        var directories = inventory.Directories.Where(path => Selected(path) && path != candidate.RootRelativePath)
            .Select(path => new PackageInputDirectory(Logical(path), inventory.IsEmpty(path)));
        var extraDirectories = inventory.Directories.Where(path => !Selected(path) && path != candidate.RootRelativePath &&
                !candidate.RootRelativePath.StartsWith(path + "/", StringComparison.Ordinal))
            .Select(path => new PackageInputDirectory(path, inventory.IsEmpty(path)));
        return new(applicationId, candidate.SequenceNumber, files, directories, extraFiles, extraDirectories);
    }

    private static async Task<Inventory> ScanAsync(string source, PackageSourceKind kind, PackageReadLimits limits, string? capture,
        CancellationToken cancellationToken)
    {
        var inventory = new Inventory(limits, kind);
        EnsureNoLinks(source);
        if (kind == PackageSourceKind.Directory)
        {
            var pending = new Stack<string>();
            pending.Push(source);
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoLinks(directory);
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoLinks(entry);
                    var relative = Path.GetRelativePath(source, entry).Replace(Path.DirectorySeparatorChar, '/');
                    var isDirectory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
                    inventory.Register(relative, isDirectory);
                    if (isDirectory) pending.Push(entry);
                    else
                    {
                        await using var input = OpenSource(entry);
                        EnsureNoLinks(entry);
                        await inventory.CaptureFileAsync(relative, input, input.Length, capture, cancellationToken).ConfigureAwait(false);
                        EnsureNoLinks(entry);
                    }
                }
                EnsureNoLinks(directory);
            }
        }
        else
        {
            await using var file = OpenSource(source);
            if (file.Length > limits.MaxArchiveBytes) throw Limit("ARCHIVE_SIZE_LIMIT", "The compressed archive exceeds the configured size limit.");
            inventory.ArchiveSha256 = await HashArchiveAsync(file, limits.MaxArchiveBytes, cancellationToken).ConfigureAwait(false);
            ZipDirectoryGuard.Validate(file, limits);
            file.Position = 0;
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > limits.MaxEntries) throw Limit("ENTRY_COUNT_LIMIT", "Too many ZIP entries.");
            // Validate the complete namespace before creating any captured payload.
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mode = (entry.ExternalAttributes >> 16) & 0xf000;
                if (mode is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new PackageInputException("ZIP-PATHS", "UNSAFE_ZIP_ENTRY", "ZIP links and special entries are not allowed.", entry.FullName);
                var directory = entry.FullName.EndsWith('/');
                if (mode == 0x4000 && !directory || mode == 0x8000 && directory)
                    throw new PackageInputException("ZIP-PATHS", "UNSAFE_ZIP_ENTRY", "ZIP type and path disagree.", entry.FullName);
                inventory.Register(directory ? entry.FullName[..^1] : entry.FullName, directory);
                if (directory && entry.Length != 0) throw new InvalidDataException("ZIP directory entries cannot carry file bytes.");
                if (entry.Length > limits.MaxFileBytes) throw Limit("FILE_SIZE_LIMIT", "A ZIP member exceeds the per-file limit.", entry.FullName);
                if (entry.Length / (double)Math.Max(1, entry.CompressedLength) > limits.MaxCompressionRatio)
                    throw Limit("COMPRESSION_RATIO_LIMIT", "A ZIP member exceeds the compression-ratio limit.", entry.FullName);
            }
            foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')))
            {
                await using var input = entry.Open();
                await inventory.CaptureFileAsync(entry.FullName, input, entry.Length, capture, cancellationToken, entry.CompressedLength).ConfigureAwait(false);
            }
            EnsureNoLinks(source);
        }
        return inventory;
    }

    private static FileStream OpenSource(string path) => ControlledInputFile.OpenRead(path);

    private static async Task<string> HashArchiveAsync(Stream file, long maxBytes, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int count;
        while ((count = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (count > maxBytes - length) throw Limit("ARCHIVE_SIZE_LIMIT", "Archive stream exceeded the configured size limit.");
            hash.AppendData(buffer.AsSpan(0, count));
            length += count;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static void EnsureNoLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new PackageInputException("INPUT-PATHS", "UNSAFE_INPUT_PATH", "Input and capture paths cannot contain links or reparse points.");
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root), comparison) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
    }

    private static PackageInputException Limit(string code, string message, string? path = null) => new("INPUT-LIMITS", code, message, path);

    private static void DeleteOwnedCapture(string owned)
    {
        // Captures are flat and contain only opaque file names. Never recursively
        // delete a supplied source, traverse a link, or accept a derived parent.
        if (!Path.GetFileName(owned).StartsWith("input-", StringComparison.Ordinal) || !Guid.TryParseExact(Path.GetFileName(owned)[6..], "N", out _))
            throw new InvalidOperationException("Invalid capture ownership identity.");
        if (!Directory.Exists(owned)) return;
        EnsureNoLinks(owned);
        foreach (var entry in Directory.EnumerateFileSystemEntries(owned))
        {
            EnsureNoLinks(entry);
            if (Directory.Exists(entry)) throw new IOException("Unexpected directory in owned flat capture; cleanup requires inspection.");
            File.Delete(entry);
        }
        Directory.Delete(owned);
    }

    private sealed class Inventory(PackageReadLimits limits, PackageSourceKind kind)
    {
        private readonly Dictionary<string, (string Path, bool Directory, bool Explicit)> _names = new(StringComparer.Ordinal);
        private readonly HashSet<string> _nonEmptyDirectories = new(StringComparer.Ordinal);
        private long _expandedBytes;
        public Dictionary<string, PackageInputFile> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> CapturedPaths { get; } = new(StringComparer.Ordinal);
        public string? ArchiveSha256 { get; set; }
        public bool IsEmpty(string directory) => !_nonEmptyDirectories.Contains(directory);

        public void Register(string path, bool directory)
        {
            try { PackageLogicalPath.Validate(path, limits.MaxPathLength, limits.MaxPathDepth); }
            catch (ArgumentException exception)
            {
                throw new PackageInputException(kind == PackageSourceKind.Zip ? "ZIP-PATHS" : "INPUT-PATHS",
                    kind == PackageSourceKind.Zip ? "UNSAFE_ZIP_ENTRY" : "UNSAFE_INPUT_PATH", exception.Message, path, exception);
            }
            var parts = path.Split('/');
            for (var index = 1; index < parts.Length; index++)
            {
                var parent = string.Join('/', parts.Take(index));
                RegisterOne(parent, true, false);
                _nonEmptyDirectories.Add(parent);
            }
            RegisterOne(path, directory, true);
        }

        private void RegisterOne(string path, bool directory, bool explicitEntry)
        {
            var key = PackageLogicalPath.CollisionKey(path, limits.MaxPathLength, limits.MaxPathDepth);
            if (_names.TryGetValue(key, out var existing))
            {
                if (existing.Path != path || existing.Directory != directory || !directory || existing.Explicit && explicitEntry)
                    throw new PackageInputException(kind == PackageSourceKind.Zip ? "ZIP-PATHS" : "INPUT-PATHS",
                        kind == PackageSourceKind.Zip ? "DUPLICATE_ZIP_ENTRY" : "DUPLICATE_INPUT_PATH",
                        "Input paths collide after portable Unicode/case normalization or file/directory comparison.", path);
                if (explicitEntry) _names[key] = (path, directory, true);
                return;
            }
            if (_names.Count >= limits.MaxEntries) throw Limit("ENTRY_COUNT_LIMIT", "The input namespace exceeds the configured entry limit.", path);
            _names.Add(key, (path, directory, explicitEntry));
            if (directory) Directories.Add(path);
        }

        public async Task CaptureFileAsync(string path, Stream input, long expectedLength, string? capture, CancellationToken cancellationToken, long? compressedLength = null)
        {
            if (expectedLength > limits.MaxFileBytes) throw Limit("FILE_SIZE_LIMIT", "A file exceeds the per-file limit.", path);
            if (expectedLength > limits.MaxExpandedBytes - _expandedBytes) throw Limit("TOTAL_SIZE_LIMIT", "Input expanded bytes exceed the total limit.", path);
            var destination = capture is null ? null : Path.Combine(capture, Guid.NewGuid().ToString("N"));
            if (capture is not null) EnsureNoLinks(capture);
            await using var output = destination is null ? null : new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            // MD5 is required by the legacy eCTD wire format; SHA-256 binds identity.
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            var buffer = new byte[64 * 1024];
            long length = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (count > expectedLength - length) throw new InvalidDataException("Stream expanded beyond its declared input length.");
                if (count > limits.MaxFileBytes - length) throw Limit("FILE_SIZE_LIMIT", "Stream exceeded the per-file limit.", path);
                if (count > limits.MaxExpandedBytes - _expandedBytes) throw Limit("TOTAL_SIZE_LIMIT", "Stream exceeded the total expanded byte limit.", path);
                if (compressedLength is { } compressed && (length + count) / (double)Math.Max(1, compressed) > limits.MaxCompressionRatio)
                    throw Limit("COMPRESSION_RATIO_LIMIT", "Stream exceeded the permitted compression ratio.", path);
                length += count;
                _expandedBytes += count;
                sha.AppendData(buffer.AsSpan(0, count));
                md5.AppendData(buffer.AsSpan(0, count));
                if (output is not null) await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            if (length != expectedLength) throw new InvalidDataException("File bytes do not match the declared input length.");
            Files.Add(path, new(path, length, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant()));
            if (destination is not null) CapturedPaths.Add(path, destination);
        }
    }

    private sealed class CapturedInput(PackageInputSelection selection, PackageSourceKind kind, PackageSequenceCandidate candidate,
        PackageInputManifest manifest, Inventory inventory, PackageReadLimits limits, string owned, TimeProvider timeProvider) : ICapturedPackageInput
    {
        private bool _disposed;
        public string SourceId => selection.SourceId;
        public PackageSourceKind SourceKind => kind;
        public PackageInputManifest Manifest => manifest;
        public string? ArchiveSha256 => inventory.ArchiveSha256;
        public string InputDigest { get; } = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, manifestDigest = manifest.Digest, archiveSha256 = inventory.ArchiveSha256
        }));
        public PackageReadLimits Limits => limits;

        public Stream OpenRead(string logicalPath)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PackageLogicalPath.Validate(logicalPath, limits.MaxPathLength, limits.MaxPathDepth);
            var prefix = manifest.SequenceNumber + "/";
            if (!logicalPath.StartsWith(prefix, StringComparison.Ordinal)) throw new FileNotFoundException("The logical path does not belong to this selected sequence.");
            var relative = (candidate.RootRelativePath.Length == 0 ? "" : candidate.RootRelativePath + "/") + logicalPath[prefix.Length..];
            if (!inventory.CapturedPaths.TryGetValue(relative, out var captured)) throw new FileNotFoundException("The selected input does not contain this exact logical path.", logicalPath);
            EnsureNoLinks(captured);
            return OpenSource(captured);
        }

        public async Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limits.MaxElapsedSeconds), timeProvider);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try { await VerifyUnchangedCoreAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw Limit("INPUT_READ_TIMEOUT", "Input verification exceeded the configured elapsed-time limit."); }
        }

        public async Task VerifyUnchangedCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                var current = await ScanAsync(selection.Path, kind, limits, null, cancellationToken).ConfigureAwait(false);
                if (CreateManifest(selection.ApplicationId, candidate, current).Digest != manifest.Digest || current.ArchiveSha256 != inventory.ArchiveSha256)
                    throw new PackageInputChangedException(selection.SourceId);
                var captured = new Inventory(limits, kind);
                foreach (var (relative, path) in inventory.CapturedPaths)
                {
                    EnsureNoLinks(path);
                    await using var input = OpenSource(path);
                    await captured.CaptureFileAsync(relative, input, input.Length, null, cancellationToken).ConfigureAwait(false);
                    if (captured.Files[relative] != inventory.Files[relative]) throw new PackageInputChangedException(selection.SourceId);
                }
            }
            catch (IOException exception) when (exception is not PackageInputChangedException)
            { throw new PackageInputChangedException(selection.SourceId); }
            catch (UnauthorizedAccessException) { throw new PackageInputChangedException(selection.SourceId); }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            DeleteOwnedCapture(owned);
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
