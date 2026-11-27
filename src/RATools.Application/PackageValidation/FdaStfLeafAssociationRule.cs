using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfLeafAssociationRule
{
    internal const string RuleId = "FDA47-1816";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF leaf association inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1816" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1816 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1816.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured STF specification is required to distinguish empty identifier-only revisions.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var observations = new Dictionary<(string Digest, string Path), int>();
            var targetBackbones = new List<ParsedBackbone>();
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                foreach (var document in xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    var key = (xml.InputDigest, document.LogicalPath);
                    observations[key] = observations.GetValueOrDefault(key) + 1;
                }
                if (xml.InputDigest != packages.Target.InputDigest || xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                    xml.RulesDigest != context.Catalog.Digest || xml.AssetManifestDigest != context.Assets.Digest || xml.LimitsDigest != packages.Target.Limits.Digest()) continue;
                foreach (var document in xml.Backbones) { Tick(); targetBackbones.Add(document); }
            }
            foreach (var bound in bindings.Documents)
            {
                Tick();
                var selected = bound.IsTarget;
                var owners = new List<StfOwnerBinding>();
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    owners.Add(owner);
                    if (owner.Qualified) selected = true;
                    else incomplete = true;
                }
                if (!selected) continue;
                applicable = true;
                var document = bound.Document;
                if (!seen.Add(document.LogicalPath) || observations.GetValueOrDefault((bound.InputDigest, document.LogicalPath)) != 1)
                { incomplete = true; continue; }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                if (roots.Count != 1 || roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                { incomplete = true; continue; }
                var found = false;
                var unknown = false;
                foreach (var reference in bound.References)
                {
                    Tick();
                    if (reference.Status == StfReferenceStatus.Resolved) found = true;
                    else if (reference.Status is not (StfReferenceStatus.InvalidReference or StfReferenceStatus.LeafMissing or
                        StfReferenceStatus.NotLeaf or StfReferenceStatus.FutureReference)) unknown = true;
                }
                // This rule asks for existence, not validity of every reference or content role.
                // Self/other-STF leaves still establish a leaf association; 1799 is independent.
                if (found) continue;
                if (unknown || !bound.StructureComplete || !document.ReadComplete || document.DtdStatus != CheckStatus.Pass)
                { incomplete = true; continue; }
                if (bound.References.Count == 0)
                {
                    if (FdaStfIdentifierRevision.Qualified(bound, owners, bindings, packages, lifecycle, context, Tick)) continue;
                    var initial = owners.Count > 0;
                    foreach (var owner in owners) { Tick(); initial &= InitialNew(owner); }
                    if (!initial) { incomplete = true; continue; }
                    // The archived specification permits empty identifier-only revisions.
                    // Neither append alone nor an absent history supplies that qualification.
                }
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "This completely assessed STF has no established leaf association.",
                    roots[0].Location with { FieldPath = "study-document/doc-content", Files = [new(bound.SourceId, document.LogicalPath)] },
                    Expected: "At least one associated leaf", Actual: "No associated leaf"));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An STF with proven absent leaf association is known; other STFs or revision exceptions may remain unqualified.", findings.ToArray()));
            }

            bool InitialNew(StfOwnerBinding owner)
            {
                Tick();
                if (!owner.Qualified || owner.Leaf.Operation != "new" || !string.IsNullOrEmpty(owner.Leaf.ModifiedFile)) return false;
                var documents = new List<ParsedBackbone>();
                foreach (var document in targetBackbones) { Tick(); if (document.LogicalPath == owner.BackboneLogicalPath) documents.Add(document); }
                if (documents.Count != 1) return false;
                var elements = new List<ParsedXmlElement>();
                foreach (var element in documents[0].Elements) { Tick(); if (element.NodePath == owner.Leaf.NodePath) elements.Add(element); }
                if (elements.Count != 1) return false;
                var operation = elements[0].Attributes.GetValueOrDefault("operation");
                if (operation is not { LocalName: "operation", NamespaceUri.Length: 0, IsDefault: false, Value: "new" }) return false;
                foreach (var attribute in elements[0].Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName == "modified-file" && attribute.NamespaceUri.Length == 0) return false;
                }
                return true;
            }
        }
        catch (LeafAssociationWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked each selected STF for at least one actual leaf association. Qualified direct identifier-only appends may inherit a still-current predecessor association. Initial empty STFs can fail; other revision chains and unavailable context remain unevaluated. Local passes do not establish full FDA 1816 coverage.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new LeafAssociationWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class LeafAssociationWorkLimitException : Exception;
}
