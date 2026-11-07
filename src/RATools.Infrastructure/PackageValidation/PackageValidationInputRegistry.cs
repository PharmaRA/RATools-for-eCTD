using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

public sealed record RegisterPackageInputRequest(Guid ApplicationId, string Path, string? SequenceNumber = null, string? RootRelativePath = null);
public sealed record PackageInputDto(Guid Id, Guid ApplicationId, string SequenceNumber, PackageSourceKind SourceKind,
    string InputDigest, DateTime CreatedUtc, int FileCount, long TotalBytes, string LimitsDigest);

public sealed class PackageValidationInputRegistry(IPackageValidationStore store, IPackageInputReader reader,
    IWorkspacePathPolicy pathPolicy, IOptions<PackageValidationOptions> options, TimeProvider? timeProvider = null,
    ILogger<PackageValidationInputRegistry>? logger = null)
{
    private static readonly Action<ILogger, string, Exception?> LogRetentionFailure = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(1, "UploadRetentionFailure"), "Failed upload retained for inspection at {UploadDirectory}");

    public Task<PackageInputDto> RegisterAsync(RegisterPackageInputRequest request, CancellationToken cancellationToken = default)
    {
        var path = pathPolicy.EnsureAllowed(request.Path);
        return CaptureAsync(Guid.NewGuid(), request with { Path = path }, owned: false, cancellationToken);
    }

    public async Task<PackageInputDto> UploadAsync(Guid applicationId, Stream content, string? sequenceNumber,
        string? rootRelativePath, CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        var id = Guid.NewGuid();
        var root = Path.GetFullPath(Path.Combine(options.Value.StorageRoot, "inputs"));
        EnsureNoLinks(root);
        var directory = Path.Combine(root, id.ToString("N"));
        Directory.CreateDirectory(directory);
        EnsureNoLinks(directory);
        var file = Path.Combine(directory, "package.zip");
        var registrationAttempted = false;
        try
        {
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                long length = 0;
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    length = checked(length + read);
                    if (length > options.Value.ReadLimits.MaxArchiveBytes)
                        throw new PackageInputException("INPUT-LIMITS", "UPLOAD_LIMIT", "The uploaded ZIP exceeds the configured archive byte limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            return await CaptureAsync(id, new(applicationId, file, sequenceNumber, rootRelativePath), owned: true, cancellationToken,
                () => registrationAttempted = true).ConfigureAwait(false);
        }
        catch (Exception uploadException)
        {
            if (!registrationAttempted)
            {
                try { RecycleFailedUpload(directory, root, id); }
                catch (Exception retentionException) when (retentionException is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Keep both the original API error and the retention diagnostic.
                    // The upload remains at its original path if a safe move is impossible.
                    uploadException.Data["UploadRetentionFailure"] = retentionException;
                    try { if (logger is not null) LogRetentionFailure(logger, directory, retentionException); }
                    catch (Exception loggingException)
                    {
                        uploadException.Data["UploadRetentionLoggingFailure"] = loggingException;
                    }
                }
            }
            // A database timeout may occur after commit; retain the input in that case.
            throw;
        }
    }

    private static void RecycleFailedUpload(string directory, string root, Guid id)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        directory = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(directory), root, comparison) || Path.GetFileName(directory) != id.ToString("N"))
            throw new InvalidOperationException("Invalid upload ownership identity.");
        EnsureNoLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureNoLinks(entry);
            if (Directory.Exists(entry) || Path.GetFileName(entry) != "package.zip")
                throw new IOException("Unexpected entry in owned upload; retention requires inspection.");
        }
        var recycleRoot = Path.Combine(root, "_recycle");
        EnsureNoLinks(recycleRoot);
        Directory.CreateDirectory(recycleRoot);
        EnsureNoLinks(recycleRoot);
        var destination = Path.GetFullPath(Path.Combine(recycleRoot, id.ToString("N") + "-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(destination), recycleRoot, comparison))
            throw new InvalidOperationException("Invalid upload recycle destination.");
        EnsureNoLinks(directory);
        Directory.Move(directory, destination);
    }

    public async Task<PackageInputDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var input = await store.GetInputAsync(id, cancellationToken).ConfigureAwait(false);
        return input is null ? null : ToDto(input);
    }

    public async Task<T> ReadFileAsync<T>(Guid id, string logicalPath, Func<PackageInputFile, Stream, Task<T>> read,
        bool surrounding = false, CancellationToken cancellationToken = default)
    {
        var input = await store.GetInputAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The registered input does not exist.");
        // Only exact manifest members are addressable; never resolve caller paths against disk.
        using var manifest = System.Text.Json.JsonDocument.Parse(input.ManifestJson);
        if (!manifest.RootElement.GetProperty(surrounding ? "additionalFiles" : "files").EnumerateArray()
            .Any(file => file.GetProperty("logicalPath").GetString() == logicalPath))
            throw new KeyNotFoundException("The exact file is not in this input's selected inventory scope.");
        if (!input.IsOwnedUpload) pathPolicy.EnsureAllowed(input.SourcePath);
        ICapturedPackageInput capture;
        try
        {
            capture = await reader.ReadAsync(new(input.ApplicationId, input.Id.ToString("D"), input.SourcePath,
                input.SequenceNumber, input.RootRelativePath), input.Limits, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception) when (exception is not PackageInputException { RuleId: "INPUT-LIMITS" })
        { throw new PackageInputChangedException(input.Id.ToString("D")); }
        await using (capture.ConfigureAwait(false))
        {
            if (capture.InputDigest != input.InputDigest || capture.SourceKind != input.SourceKind)
                throw new PackageInputChangedException(input.Id.ToString("D"));
            await capture.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
            var file = (surrounding ? capture.Manifest.AdditionalFiles : capture.Manifest.Files).Single(file => file.LogicalPath == logicalPath);
            await using var stream = surrounding ? capture.OpenAdditionalRead(logicalPath) : capture.OpenRead(logicalPath);
            return await read(file, stream).ConfigureAwait(false);
        }
    }

    private async Task<PackageInputDto> CaptureAsync(Guid id, RegisterPackageInputRequest request, bool owned, CancellationToken cancellationToken,
        Action? beforeRegistration = null)
    {
        var limits = options.Value.ReadLimits;
        await using var captured = await reader.ReadAsync(new(request.ApplicationId, id.ToString("D"), request.Path,
            request.SequenceNumber, request.RootRelativePath), limits, cancellationToken).ConfigureAwait(false);
        var manifest = System.Text.Json.JsonSerializer.SerializeToNode(captured.Manifest, PackageValidationJson.Options)!.AsObject();
        manifest["capture"] = System.Text.Json.JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1, captured.InputDigest, captured.ArchiveSha256, captured.SourceKind, limitsDigest = limits.Digest()
        }, PackageValidationJson.Options);
        var input = new RegisteredPackageInput(id, request.ApplicationId, captured.Manifest.SequenceNumber,
            request.Path, request.RootRelativePath, captured.SourceKind, captured.InputDigest,
            manifest.ToJsonString(PackageValidationJson.Options), limits, (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, owned);
        beforeRegistration?.Invoke();
        await store.RegisterInputAsync(input, cancellationToken).ConfigureAwait(false);
        return ToDto(input);
    }

    private static PackageInputDto ToDto(RegisteredPackageInput input)
    {
        // Avoid deserializing immutable domain constructors through an alternate contract.
        using var manifest = System.Text.Json.JsonDocument.Parse(input.ManifestJson);
        var files = manifest.RootElement.GetProperty("files");
        return new(input.Id, input.ApplicationId, input.SequenceNumber, input.SourceKind, input.InputDigest, input.CreatedUtc,
            files.GetArrayLength(), files.EnumerateArray().Sum(file => file.GetProperty("length").GetInt64()), input.Limits.Digest());
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Validation input storage must not traverse a link or reparse point.");
    }
}
