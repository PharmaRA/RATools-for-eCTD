namespace RATools.Domain.PackageValidation;

// The complete G0 binding is compared before reusing reports or manual evidence.
public sealed record ValidationBinding
{
    public ValidationBinding(string inputManifestDigest, string historyBaselineDigest, string profileSnapshotId,
        string rulesDigest, string engineVersion, PackageValidationMode validationMode, string resourceLimitsDigest)
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
    }

    public string InputManifestDigest { get; }
    public string HistoryBaselineDigest { get; }
    public string ProfileSnapshotId { get; }
    public string RulesDigest { get; }
    public string EngineVersion { get; }
    public PackageValidationMode ValidationMode { get; }
    public string ResourceLimitsDigest { get; }

    public static void RequireDigest(string digest)
    {
        if (digest is null || digest.Length != 64 || digest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A digest must contain 64 lowercase hexadecimal characters.", nameof(digest));
    }
}
