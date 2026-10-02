using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public enum PackageLeafEffectiveness { Current, Replaced, Deleted, DeleteEvent, Unknown }

public sealed record PackageLifecycleEvent(ParsedPackageLeaf Source, LeafAddress? Target,
    string? TargetContentLogicalPath, CheckStatus Status, IReadOnlyList<ValidationFinding> Findings);

public sealed record PackageLeafState(LeafAddress Address, PackageLeafEffectiveness Effectiveness,
    bool IsRegionalReference);

// Facts relative to the explicitly selected baseline, not a qualified cumulative review or submission record.
public sealed class PackageLifecycleInspection
{
    internal PackageLifecycleInspection(PackageInputSet inputs, string profile, IEnumerable<PackageXmlInspection> xml,
        IEnumerable<PackageLifecycleEvent> events, IEnumerable<PackageLeafState> states, IEnumerable<RuleCheckResult> checks)
    {
        InputDigest = inputs.Target.InputDigest;
        HistoryManifestDigest = inputs.Baseline.Digest;
        ProfileSnapshotId = profile;
        LimitsDigest = inputs.Target.Limits.Digest();
        XmlInspections = Array.AsReadOnly(xml.ToArray());
        Events = Array.AsReadOnly(events.ToArray());
        States = Array.AsReadOnly(states.OrderBy(state => state.Address.SequenceNumber, StringComparer.Ordinal)
            .ThenBy(state => state.Address.BackboneRelativePath, StringComparer.Ordinal)
            .ThenBy(state => state.Address.LeafId, StringComparer.Ordinal).ToArray());
        Checks = Array.AsReadOnly(checks.ToArray());
    }

    public int SchemaVersion { get; } = 1;
    public string EngineVersion { get; } = "package-lifecycle-inspector-v1";
    public string RulesDigest { get; } = PackageValidationCatalog.Current.Digest;
    public string AssetManifestDigest { get; } = PackageXmlAssets.Current.Digest;
    public string InputDigest { get; }
    public string HistoryManifestDigest { get; }
    public string ProfileSnapshotId { get; }
    public string LimitsDigest { get; }
    public IReadOnlyList<PackageXmlInspection> XmlInspections { get; }
    public IReadOnlyList<PackageLifecycleEvent> Events { get; }
    public IReadOnlyList<PackageLeafState> States { get; }
    public IReadOnlyList<RuleCheckResult> Checks { get; }
    public bool ResolutionComplete => Checks.All(check => check.Status is CheckStatus.Pass or CheckStatus.NotApplicable);
}
