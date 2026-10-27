using RATools.Domain.Ctd;

namespace RATools.Domain.PackageValidation;

// Projection algorithm identity. A change to how events are applied must change this
// value so cached or archived projections are visibly stale rather than silently mixed.
public static class DossierProjectionVersion
{
    public const string Current = "dossier-projection-v2";
}

public enum DossierView { Sequence, Current, History }

// DeleteEvent is the delete instruction itself: retained in the history as an event,
// never an effective delivered leaf. Unknown covers leaves whose event did not resolve.
public enum DossierLeafStatus { Current, Replaced, Deleted, DeleteEvent, AppendMember, Unknown }

// One effective (or historical) leaf in the cumulative view. Address is the immutable
// LeafAddress; Title/File come from the source XML facts and are never re-derived from
// the mutable workspace.
public sealed record DossierLeaf(LeafAddress Address, string? Title, string? Operation, string? ContentLogicalPath,
    DossierLeafStatus Status, string? ReplacedBy, string? DeletedBy, string? AppendedTo, bool IsRegionalReference,
    string SourceSequence);

// A resolved lifecycle edge. Unresolved edges are retained with a reason instead of
// being dropped, so an incomplete projection cannot look complete.
public sealed record DossierEvent(LeafAddress Source, LeafAddress? Target, string Operation, CheckStatus Status,
    string? UnresolvedReason);

public sealed record DossierBaselineEntry(string SequenceNumber, string SourceKind, string SourceId,
    string ContentDigest, string ProfileSnapshotId, string TrustStatus);

// The projection is derived from an explicitly selected, verified input set. Completeness
// is a first-class fact: incomplete projections can be shown but never authorize a
// finalization decision.
public sealed class DossierProjection
{
    private DossierProjection(Guid applicationId, string targetSequence, string profileSnapshotId, string inputDigest,
        string baselineDigest, string limitsDigest, string engineVersion, bool isComplete, IReadOnlyList<string> incompleteReasons,
        IReadOnlyList<DossierBaselineEntry> baseline, IReadOnlyList<DossierLeaf> leaves, IReadOnlyList<DossierEvent> events,
        IReadOnlyList<ValidationFinding> findings, IEnumerable<string> appliedSequences)
    {
        ApplicationId = applicationId;
        TargetSequence = targetSequence;
        ProfileSnapshotId = profileSnapshotId;
        InputDigest = inputDigest;
        BaselineDigest = baselineDigest;
        LimitsDigest = limitsDigest;
        EngineVersion = engineVersion;
        IsComplete = isComplete;
        IncompleteReasons = Array.AsReadOnly(incompleteReasons.ToArray());
        Baseline = Array.AsReadOnly(baseline.ToArray());
        Leaves = Array.AsReadOnly(leaves.ToArray());
        Events = Array.AsReadOnly(events.ToArray());
        Findings = Array.AsReadOnly(findings.ToArray());
        AppliedSequences = Array.AsReadOnly(appliedSequences.Order(StringComparer.Ordinal).ToArray());
    }

    public int SchemaVersion { get; } = 1;
    public Guid ApplicationId { get; }
    public string TargetSequence { get; }
    public string ProfileSnapshotId { get; }
    public string InputDigest { get; }
    public string BaselineDigest { get; }
    public string LimitsDigest { get; }
    public string EngineVersion { get; }
    public bool IsComplete { get; }
    public IReadOnlyList<string> IncompleteReasons { get; }
    public IReadOnlyList<DossierBaselineEntry> Baseline { get; }
    public IReadOnlyList<DossierLeaf> Leaves { get; }
    public IReadOnlyList<DossierEvent> Events { get; }
    public IReadOnlyList<ValidationFinding> Findings { get; }
    public IReadOnlyList<string> AppliedSequences { get; }

    // Current view: leaves whose final status is Current or AppendMember, in address order.
    public IReadOnlyList<DossierLeaf> EffectiveLeaves => Leaves
        .Where(leaf => leaf.Status is DossierLeafStatus.Current or DossierLeafStatus.AppendMember)
        .OrderBy(leaf => leaf.Address.SequenceNumber, StringComparer.Ordinal)
        .ThenBy(leaf => leaf.Address.BackboneRelativePath, StringComparer.Ordinal)
        .ThenBy(leaf => leaf.Address.LeafId, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<DossierLeaf> LeavesFor(string sequenceNumber) =>
        Leaves.Where(leaf => leaf.Address.SequenceNumber == sequenceNumber).ToArray();

    // Changed = added by the target sequence plus leaves replaced/deleted by it.
    public IReadOnlyList<DossierLeaf> ChangedInTarget => Leaves
        .Where(leaf => leaf.Address.SequenceNumber == TargetSequence || leaf.ReplacedBy?.StartsWith(TargetSequence + "/", StringComparison.Ordinal) == true ||
            leaf.DeletedBy?.StartsWith(TargetSequence + "/", StringComparison.Ordinal) == true).ToArray();

    public static DossierProjection Create(Guid applicationId, string targetSequence, string profileSnapshotId,
        string inputDigest, string baselineDigest, string limitsDigest, string engineVersion, bool isComplete,
        IEnumerable<string> incompleteReasons, HistoryBaselineManifest baseline, IEnumerable<DossierLeaf> leaves,
        IEnumerable<DossierEvent> events, IEnumerable<ValidationFinding> findings, IEnumerable<string> appliedSequences)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(incompleteReasons);
        if (applicationId != baseline.ApplicationId || !string.Equals(targetSequence, baseline.TargetSequence, StringComparison.Ordinal))
            throw new ArgumentException("A projection must bind the exact application and target sequence of its baseline.", nameof(baseline));
        var ordered = leaves.OrderBy(leaf => leaf.Address.SequenceNumber, StringComparer.Ordinal)
            .ThenBy(leaf => leaf.Address.BackboneRelativePath, StringComparer.Ordinal)
            .ThenBy(leaf => leaf.Address.LeafId, StringComparer.Ordinal).ToArray();
        if (ordered.Any(leaf => leaf.Address.ApplicationId != applicationId))
            throw new ArgumentException("Every leaf must belong to the projection's application.", nameof(leaves));
        if (ordered.Select(leaf => leaf.Address).Distinct().Count() != ordered.Length)
            throw new ArgumentException("A leaf address may appear only once in a projection.", nameof(leaves));
        var entries = baseline.Entries.Select(entry => new DossierBaselineEntry(entry.SequenceNumber,
            entry.SourceKind.ToString(), entry.SourceId, entry.ContentDigest, entry.ProfileSnapshotId, entry.TrustStatus.ToString())).ToArray();
        var copy = incompleteReasons.ToArray();
        return new DossierProjection(applicationId, targetSequence, profileSnapshotId, inputDigest, baselineDigest, limitsDigest,
            engineVersion, isComplete && copy.Length == 0, copy, entries, ordered, events.ToArray(), findings.ToArray(), appliedSequences);
    }
}
