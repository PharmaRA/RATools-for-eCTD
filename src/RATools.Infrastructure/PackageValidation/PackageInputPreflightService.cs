using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PackageInputPreflightRequest(Guid ApplicationId, string Path, PackageInputRole Role,
    string ProfileSnapshotId, string? StandardsSnapshotId = null, DateOnly? SelectionDate = null)
{
    [JsonRequired]
    public PackageInputRole Role { get; init; } = Role;
}
public sealed record PackageInputPreflightReceipt(string Id, JsonElement Assessment);

public sealed class PackageInputPreflightService(IWorkspacePathPolicy pathPolicy, IOptions<PackageValidationOptions> options,
    StandardsSnapshotService snapshots, TimeProvider? clock = null)
{
    private const int MaxEvidenceBytes = 64 * 1024 * 1024;
    public async Task<PackageInputPreflightReceipt> InspectAsync(PackageInputPreflightRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Role)) throw new ArgumentOutOfRangeException(nameof(request));
        var path = pathPolicy.EnsureAllowed(request.Path);
        var today = DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        if (request.SelectionDate is not null && request.SelectionDate != today)
            throw new StandardsSelectionException(["SelectionDateInvalid: New input assessments must select today's policy."]);
        var catalog = PackageValidationCatalog.Current;
        _ = catalog.ForProfile(request.ProfileSnapshotId);
        var snapshot = request.StandardsSnapshotId is { } id ? await snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false) :
            await snapshots.CaptureAsync(catalog.Profiles[request.ProfileSnapshotId].TemplateKey, request.ProfileSnapshotId, cancellationToken).ConfigureAwait(false);
        if (snapshot.RuleProfileAlias != request.ProfileSnapshotId) throw new StandardsSelectionException(["RuleProfileMismatch: The exact snapshot has a different rule profile alias."]);
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: today);
        var storage = Path.GetFullPath(options.Value.StorageRoot);
        if (PackageInputReader.IsWithin(path, storage) || PackageInputReader.IsWithin(storage, path))
            throw new ArgumentException("Input and preflight evidence storage must not overlap.", nameof(request));
        var reader = new PackageInputReader(Path.Combine(storage, "captures"), clock);
        var evidence = await reader.InspectShapeAsync(new(request.ApplicationId, "preflight-input", path), request.Role, options.Value.ReadLimits, cancellationToken).ConfigureAwait(false);
        var checks = FdaInputShapeRules.Inspect(evidence, request.ProfileSnapshotId, context, options.Value.ReadLimits.MaxXmlFindings - 1, cancellationToken);
        var assessment = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, scope = "InputShapeOnly", isReadyForFinalization = false,
            applicationId = request.ApplicationId, evidence.InputDigest, evidence,
            profileSnapshotId = request.ProfileSnapshotId, standardsSnapshotId = snapshot.Id, rulesDigest = context.Catalog.Digest,
            selectionDate = today, engineVersion = "package-input-preflight-v1", readLimits = options.Value.ReadLimits,
            ruleDescriptors = context.Catalog.ForProfile(request.ProfileSnapshotId).Where(rule => rule.InternalRuleId is "FDA47-3" or "FDA47-4"),
            sources = context.Catalog.Sources.Values.Where(source => source.Id == "fda-validation-criteria-4.7"), checks
        }, PackageValidationJson.Options);
        var json = assessment.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxEvidenceBytes) throw new PackageInputException("INPUT-LIMITS", "EVIDENCE_SIZE_LIMIT", "Input assessment evidence exceeds its storage limit.");
        var digest = PackageValidationJson.Hash(json);
        var directory = EvidenceDirectory(create: true);
        var file = Path.Combine(directory, digest + ".json");
        var pending = Path.Combine(directory, digest + "." + Guid.NewGuid().ToString("N") + ".pending");
        // An interrupted write remains a non-addressable pending file. Never overwrite evidence.
        await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
        {
            await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        PackageInputReader.EnsureNoLinks(pending);
        try { File.Move(pending, file, overwrite: false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && File.Exists(file))
        {
            var existing = await GetAsync(digest, cancellationToken).ConfigureAwait(false);
            if (existing?.Assessment.GetRawText() != json) throw new InvalidDataException("Existing preflight evidence differs.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(digest, assessment);
    }

    public async Task<PackageInputPreflightReceipt?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (id is not { Length: 64 } || !id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Use an exact lowercase SHA-256 evidence ID.", nameof(id));
        var file = Path.Combine(EvidenceDirectory(create: false), id + ".json");
        if (!File.Exists(file)) return null;
        await using var stream = ControlledInputFile.OpenRead(file);
        if (stream.Length > MaxEvidenceBytes) throw new InvalidDataException("Input assessment evidence exceeds its read limit.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input assessment content differs from its evidence ID.");
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 || document.RootElement.GetProperty("scope").GetString() != "InputShapeOnly" ||
            document.RootElement.GetProperty("isReadyForFinalization").GetBoolean()) throw new InvalidDataException("Unsupported preflight evidence schema.");
        return new(id, document.RootElement.Clone());
    }

    private string EvidenceDirectory(bool create)
    {
        var directory = Path.GetFullPath(Path.Combine(options.Value.StorageRoot, "preflight"));
        for (var path = directory; path is not null; path = Path.GetDirectoryName(path))
            if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Preflight storage must not traverse links.");
        if (create) Directory.CreateDirectory(directory);
        if (Directory.Exists(directory)) PackageInputReader.EnsureNoLinks(directory);
        return directory;
    }
}
