using System.Security.Cryptography;
using System.Text.Json.Serialization;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.RegionalMetadata;

// Exact acquired XML bytes, distinct from normalized archival backbones. Legacy
// imports have no row: reconstructed bytes must never be represented as original.
public sealed class ImportedRegionalEvidence
{
    private readonly byte[] _originalBytes;
    [JsonConstructor]
    public ImportedRegionalEvidence(Guid id, Guid applicationId, string sequenceNumber, Guid metadataId,
        string standardsSnapshotId, string relativePath, byte[] originalBytes, string sha256)
    {
        if (id == Guid.Empty || applicationId == Guid.Empty || metadataId == Guid.Empty) throw new ArgumentException("Import evidence requires exact scope identities.");
        PackageInputManifest.RequireSequenceNumber(sequenceNumber);
        if (!standardsSnapshotId.StartsWith("sha256:", StringComparison.Ordinal)) throw new ArgumentException("An exact standards snapshot is required.");
        ValidationBinding.RequireDigest(standardsSnapshotId[7..]);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 230 || relativePath.StartsWith('/') || relativePath.Contains(':') || relativePath.Contains('\\') || relativePath.Split('/').Any(part => part is "" or "." or "..")) throw new ArgumentException("Evidence path must be a bounded sequence-relative path.");
        if (originalBytes is null || originalBytes.Length is < 1 or > 8 * 1024 * 1024) throw new ArgumentException("Original XML evidence exceeds its 8 MiB bound.");
        ValidationBinding.RequireDigest(sha256);
        if (Hash(originalBytes) != sha256) throw new InvalidDataException("Original XML evidence failed SHA-256 verification.");
        Id = id; ApplicationId = applicationId; SequenceNumber = sequenceNumber; MetadataId = metadataId;
        StandardsSnapshotId = standardsSnapshotId; RelativePath = relativePath; _originalBytes = (byte[])originalBytes.Clone(); Sha256 = sha256;
    }
    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    public Guid MetadataId { get; }
    public string StandardsSnapshotId { get; }
    public string RelativePath { get; }
    public byte[] OriginalBytes => (byte[])_originalBytes.Clone();
    [JsonIgnore] public int ByteLength => _originalBytes.Length;
    public string Sha256 { get; }
    public void Verify() { if (Hash(_originalBytes) != Sha256) throw new InvalidDataException("Original XML evidence failed SHA-256 verification."); }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
