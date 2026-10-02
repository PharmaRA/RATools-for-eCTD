using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record PackageFileReference(string SourceLogicalPath, string Reference, PackageReference? Resolved,
    ValidationLocation Location, bool Exists, string Kind = "Leaf");

public sealed class PackageFileInspection
{
    internal PackageFileInspection(PackageDeliveryInput input, PackageLifecycleInspection lifecycle,
        IEnumerable<PackageFileReference> references, IEnumerable<RuleCheckResult> checks)
    {
        InputDigest = input.InputDigest;
        PrimaryInputDigest = input.Packages.Target.InputDigest;
        ComparisonInputDigest = input.Comparison?.InputDigest;
        HistoryManifestDigest = input.Packages.Baseline.Digest;
        ProfileSnapshotId = lifecycle.ProfileSnapshotId;
        LimitsDigest = input.Packages.Target.Limits.Digest();
        References = Array.AsReadOnly(references.ToArray());
        Checks = Array.AsReadOnly(checks.ToArray());
    }

    public int SchemaVersion { get; } = 1;
    public string EngineVersion { get; } = "package-file-inspector-v1";
    public string RulesDigest { get; } = PackageValidationCatalog.Current.Digest;
    public string AssetManifestDigest { get; } = PackageXmlAssets.Current.Digest;
    public string InputDigest { get; }
    public string PrimaryInputDigest { get; }
    public string? ComparisonInputDigest { get; }
    public string HistoryManifestDigest { get; }
    public string ProfileSnapshotId { get; }
    public string LimitsDigest { get; }
    public IReadOnlyList<PackageFileReference> References { get; }
    public IReadOnlyList<RuleCheckResult> Checks { get; }
}
