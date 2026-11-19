using RATools.Domain.Common;
using RATools.Domain.PackageValidation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RATools.Application.PackageValidation;

[JsonConverter(typeof(PackageInputRoleConverter))]
public enum PackageInputRole { Directory, ZipContainer, SingleFile }

public sealed class PackageInputRoleConverter() : JsonStringEnumConverter<PackageInputRole>(allowIntegerValues: false);

public sealed record PackageInputShapeEvidence(Guid ApplicationId, string SourceId, PackageInputRole Role,
    IReadOnlyList<PackageInputFile> Files, IReadOnlyList<string> Directories, string? ArchiveSha256, string LimitsDigest,
    DateTimeOffset ObservedUtc = default)
{
    public int SchemaVersion { get; } = 1;
    public bool ScanComplete { get; } = true;
    public bool StabilityVerified { get; } = true;
    public string InputDigest => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
    {
        schemaVersion = SchemaVersion, applicationId = ApplicationId, role = Role.ToString(),
        files = Files.OrderBy(item => item.LogicalPath, StringComparer.Ordinal), directories = Directories.Order(StringComparer.Ordinal),
        archiveSha256 = ArchiveSha256, limitsDigest = LimitsDigest, scanComplete = ScanComplete, stabilityVerified = StabilityVerified
    }));
}
