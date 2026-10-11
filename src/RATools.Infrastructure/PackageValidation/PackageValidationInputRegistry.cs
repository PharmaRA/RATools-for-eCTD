using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

public sealed record RegisterPackageInputRequest(Guid ApplicationId, string Path, string? SequenceNumber = null, string? RootRelativePath = null);
public sealed record PackageInputDto(Guid Id, Guid ApplicationId, string SequenceNumber, PackageSourceKind SourceKind,
    string InputDigest, DateTime CreatedUtc, int FileCount, long TotalBytes, string LimitsDigest);

public sealed class PackageValidationInputRegistry(IPackageValidationStore store, IPackageInputReader reader,
    IWorkspacePathPolicy pathPolicy, IOptions<PackageValidationOptions> options, TimeProvider? timeProvider = null)
{
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
        catch
        {
            if (!registrationAttempted)
            {
                EnsureNoLinks(directory);
                File.Delete(file);
                Directory.Delete(directory); // Only this new, empty upload directory; no recursive cleanup.
            }
            // A database timeout may occur after commit; retain the input in that case.
            throw;
        }
    }

    public async Task<PackageInputDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var input = await store.GetInputAsync(id, cancellationToken).ConfigureAwait(false);
        return input is null ? null : ToDto(input);
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
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Validation input storage must not traverse a link or reparse point.");
    }
}
