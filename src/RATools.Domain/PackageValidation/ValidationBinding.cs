using System.Text.Json.Serialization;
using RATools.Domain.Standards;

namespace RATools.Domain.PackageValidation;

// The complete G0 binding is compared before reusing reports or manual evidence.
public sealed record ValidationBinding
{
    public ValidationBinding(string inputManifestDigest, string historyBaselineDigest, string profileSnapshotId,
        string rulesDigest, string engineVersion, PackageValidationMode validationMode, string resourceLimitsDigest,
        string? standardsSnapshotId = null, DateOnly? selectionDate = null, StandardsBusinessContext? standardsBusinessContext = null)
    {
        RequireDigest(inputManifestDigest);
        RequireDigest(historyBaselineDigest);
        RequireDigest(rulesDigest);
        RequireDigest(resourceLimitsDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileSnapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        if (!Enum.IsDefined(validationMode)) throw new ArgumentOutOfRangeException(nameof(validationMode));
        InputManifestDigest = inputManifestDigest;
        HistoryBaselineDigest = historyBaselineDigest;
        ProfileSnapshotId = profileSnapshotId;
        RulesDigest = rulesDigest;
        EngineVersion = engineVersion;
        ValidationMode = validationMode;
        ResourceLimitsDigest = resourceLimitsDigest;
        if (standardsSnapshotId is not null)
        { if (!standardsSnapshotId.StartsWith("sha256:", StringComparison.Ordinal)) throw new ArgumentException("A content addressed standards identity is required.", nameof(standardsSnapshotId)); RequireDigest(standardsSnapshotId[7..]); }
        if (standardsSnapshotId is not null && selectionDate is null) throw new ArgumentException("An exact snapshot requires a bound selection date.", nameof(selectionDate));
        StandardsSnapshotId = standardsSnapshotId; SelectionDate = selectionDate; StandardsBusinessContext = standardsBusinessContext;
    }

    public string InputManifestDigest { get; }
    public string HistoryBaselineDigest { get; }
    public string ProfileSnapshotId { get; }
    public string RulesDigest { get; }
    public string EngineVersion { get; }
    public PackageValidationMode ValidationMode { get; }
    public string ResourceLimitsDigest { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StandardsSnapshotId { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateOnly? SelectionDate { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StandardsBusinessContext? StandardsBusinessContext { get; }

    public static void RequireDigest(string digest)
    {
        if (digest is null || digest.Length != 64 || digest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A digest must contain 64 lowercase hexadecimal characters.", nameof(digest));
    }
}
