using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// Builds the cumulative view from the lifecycle facts of one verified inspection. The
// builder never re-reads files and never consults the mutable workspace: its only inputs
// are the captured, verified lifecycle inspection and its explicit baseline.
//
// A projection is complete only when every applied event resolved and every inspected
// sequence was fully readable under the selected profile. Unresolved events are kept as
// Unknown/NotEvaluated so an incomplete view cannot masquerade as an effective dossier.
public static class DossierProjectionBuilder
{
    public static DossierProjection Build(PackageLifecycleInspection lifecycle, PackageDeliveryInput delivery)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(delivery);
        PackageInputSet inputs = delivery.Packages;
        var reasons = new List<string>();
        var sequences = inputs.Sequences.Keys.Order(StringComparer.Ordinal).ToArray();
        if (lifecycle.InputDigest != inputs.Target.InputDigest || lifecycle.HistoryManifestDigest != inputs.Baseline.Digest)
            throw new ArgumentException("The lifecycle facts do not belong to this exact input set.", nameof(lifecycle));

        // Expand every event into the effective leaf map, in sequence order. Within one
        // sequence all references are resolved before effectiveness is applied; the
        // inspector already did that, so the ordering here is only the sequence order.
        var leaves = new Dictionary<LeafAddress, MutableLeaf>();
        var events = new List<DossierEvent>();
        foreach (var sequence in sequences)
        {
            var xml = lifecycle.XmlInspections.FirstOrDefault(item => item.Backbones.Any(document =>
                document.LogicalPath.Split('/')[0] == sequence));
            if (xml is null)
            {
                reasons.Add($"{sequence}: no readable backbone was inspected.");
                continue;
            }
            foreach (var document in xml.Backbones.Where(document => document.LogicalPath.Split('/')[0] == sequence))
            {
                foreach (var leaf in document.Leaves)
                {
                    if (leaf.Address is not { } address)
                    {
                        reasons.Add($"{document.LogicalPath}: a leaf has no usable address.");
                        continue;
                    }
                    // Historical leaves participate in the projection: replace/delete
                    // events from later sequences must update their status.
                    if (!leaves.TryAdd(address, new MutableLeaf(address, leaf.Title, leaf.Operation, ContentPath(leaf),
                            leaf.IsRegionalReference, sequence)))
                        reasons.Add($"{document.LogicalPath}: duplicate leaf address {Address(address)}.");
                }
            }
        }

        foreach (var item in lifecycle.Events)
        {
            var source = item.Source.Address;
            if (source is null)
            {
                reasons.Add($"{item.Source.Location.LogicalPath}: a lifecycle event has no usable source address.");
                continue;
            }
            var target = item.Target;
            var status = item.Status;
            string? unresolved = status switch
            {
                CheckStatus.Pass => null,
                CheckStatus.NotEvaluated => "The operation could not be resolved completely; it is retained but not applied.",
                _ => "The operation failed validation and is retained without being applied."
            };
            if (unresolved is not null)
            {
                reasons.Add($"{source.SequenceNumber}/{source.BackboneRelativePath}#{source.LeafId}: {unresolved}");
                if (target is not null && status != CheckStatus.Pass &&
                    leaves.TryGetValue(target, out var targetLeaf) && targetLeaf.Status == DossierLeafStatus.Current)
                    targetLeaf.Status = DossierLeafStatus.Unknown;
            }
            events.Add(new DossierEvent(source, target, item.Source.Operation ?? "unknown", status, unresolved));
            if (status != CheckStatus.Pass) continue;
            if (!leaves.TryGetValue(source, out var leaf))
            {
                // A delete event has no content in the current sequence; it is still a
                // valid event but its "source" leaf may not be a delivered file.
                continue;
            }
            switch (item.Source.Operation)
            {
                case "new":
                    leaf.Status = DossierLeafStatus.Current;
                    break;
                case "replace" when target is not null:
                    leaf.Status = DossierLeafStatus.Current;
                    if (leaves.TryGetValue(target, out var replaced))
                    {
                        replaced.Status = DossierLeafStatus.Replaced;
                        replaced.ReplacedBy = Address(source);
                    }
                    break;
                case "append" when target is not null:
                    leaf.Status = DossierLeafStatus.AppendMember;
                    leaf.AppendedTo = Address(target);
                    if (leaves.TryGetValue(target, out var appended) && appended.Status == DossierLeafStatus.Current)
                        appended.Status = DossierLeafStatus.AppendMember;
                    break;
                case "delete" when target is not null:
                    if (leaves.TryGetValue(target, out var deleted))
                    {
                        deleted.Status = DossierLeafStatus.Deleted;
                        deleted.DeletedBy = Address(source);
                    }
                    break;
            }
        }

        var profile = lifecycle.ProfileSnapshotId;
        return DossierProjection.Create(inputs.Target.Manifest.ApplicationId, inputs.Target.Manifest.SequenceNumber, profile,
            delivery.InputDigest, inputs.Baseline.Digest, inputs.Target.Limits.Digest(),
            lifecycle.EngineVersion + ";" + DossierProjectionVersion.Current, reasons.Count == 0, reasons,
            inputs.Baseline, leaves.Values.Select(leaf => leaf.ToLeaf()), events,
            lifecycle.Checks.SelectMany(check => check.Findings), sequences);
    }

    private static string? ContentPath(ParsedPackageLeaf leaf) => leaf.Href is null
        ? null
        : leaf.Address is null ? null : leaf.Address.SequenceNumber + "/" + leaf.Address.BackboneRelativePath;

    private static string Address(LeafAddress address) =>
        $"{address.SequenceNumber}/{address.BackboneRelativePath}#{address.LeafId}";

    private sealed class MutableLeaf(LeafAddress address, string? title, string? operation, string? contentPath,
        bool isRegionalReference, string sourceSequence)
    {
        public LeafAddress Address { get; } = address;
        public string? Title { get; } = title;
        public string? Operation { get; } = operation;
        public string? ContentPath { get; } = contentPath;
        public bool IsRegionalReference { get; } = isRegionalReference;
        public string SourceSequence { get; } = sourceSequence;
        public DossierLeafStatus Status { get; set; } = DossierLeafStatus.Current;
        public string? ReplacedBy { get; set; }
        public string? DeletedBy { get; set; }
        public string? AppendedTo { get; set; }

        public DossierLeaf ToLeaf() => new(Address, Title, Operation, ContentPath, Status, ReplacedBy, DeletedBy,
            AppendedTo, IsRegionalReference, SourceSequence);
    }
}
