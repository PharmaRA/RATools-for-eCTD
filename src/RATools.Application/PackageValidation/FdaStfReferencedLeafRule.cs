using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfReferencedLeafRule
{
    internal const string RuleId = "FDA47-1833";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF referenced leaf existence inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1833" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1833 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1833.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var observations = new Dictionary<(string Digest, string Path), int>();
            var xmlByDigest = new Dictionary<string, List<PackageXmlInspection>>(StringComparer.Ordinal);
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                if (!xmlByDigest.TryGetValue(xml.InputDigest, out var matches)) xmlByDigest.Add(xml.InputDigest, matches = []);
                matches.Add(xml);
                foreach (var document in xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    var key = (xml.InputDigest, document.LogicalPath);
                    observations[key] = observations.GetValueOrDefault(key) + 1;
                }
            }
            foreach (var bound in bindings.Documents)
            {
                Tick();
                var selected = bound.IsTarget;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    if (owner.Qualified) selected = true;
                    else incomplete = true;
                }
                if (!selected) continue;
                applicable = true;
                if (observations.GetValueOrDefault((bound.InputDigest, bound.Document.LogicalPath)) != 1)
                { incomplete = true; continue; }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in bound.Document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                if (roots.Count != 1 || roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                { incomplete = true; continue; }
                incomplete |= !bound.StructureComplete || !bound.Document.ReadComplete || bound.Document.DtdStatus != CheckStatus.Pass;
                foreach (var reference in bound.References)
                {
                    Tick();
                    if (reference.Target is not { } address || reference.Status is not (StfReferenceStatus.Resolved or
                        StfReferenceStatus.LeafMissing or StfReferenceStatus.NotLeaf or StfReferenceStatus.LeafUnqualified))
                    { incomplete = true; continue; }
                    var status = Exists(address);
                    if (status == CheckStatus.Pass) continue;
                    if (status == CheckStatus.NotEvaluated) { incomplete = true; continue; }
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    var targetPath = address.SequenceNumber + "/" + address.BackboneRelativePath;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "This STF reference does not identify a leaf element in the completely assessed target backbone.",
                        reference.Element.Location with { FieldPath = "doc-content/@xlink:href", LeafId = null,
                            Files = [new(bound.SourceId, bound.Document.LogicalPath), new(packages.Sequences[address.SequenceNumber].SourceId, targetPath)] },
                        Expected: "Existing leaf at " + targetPath + "#" + address.LeafId, Actual: reference.Href));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A referenced leaf is proven absent; other references may remain unqualified.", findings.ToArray()));
                }
            }

            CheckStatus Exists(LeafAddress address)
            {
                Tick();
                if (!packages.Sequences.TryGetValue(address.SequenceNumber, out var capture) ||
                    !xmlByDigest.TryGetValue(capture.InputDigest, out var matches) || matches.Count != 1) return CheckStatus.NotEvaluated;
                var xml = matches[0];
                var selected = context.ForSequence(address.SequenceNumber);
                var profile = address.SequenceNumber == packages.Target.Manifest.SequenceNumber ? lifecycle.ProfileSnapshotId :
                    packages.Baseline.Entries.Single(entry => entry.SequenceNumber == address.SequenceNumber).ProfileSnapshotId;
                if (xml.ProfileSnapshotId != profile || xml.RulesDigest != selected.Catalog.Digest || xml.AssetManifestDigest != selected.Assets.Digest ||
                    xml.LimitsDigest != capture.Limits.Digest()) return CheckStatus.NotEvaluated;
                var path = address.SequenceNumber + "/" + address.BackboneRelativePath;
                var documents = new List<ParsedBackbone>();
                foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == path) documents.Add(document); }
                if (documents.Count != 1 || !documents[0].ReadComplete || documents[0].DtdStatus != CheckStatus.Pass) return CheckStatus.NotEvaluated;
                var target = documents[0];
                var originalIds = new List<(ParsedXmlElement Element, ParsedXmlAttributeValue Attribute)>();
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in target.Elements)
                {
                    Tick();
                    if (!paths.Add(element.NodePath)) return CheckStatus.NotEvaluated;
                    foreach (var attribute in element.Attributes.Values)
                    {
                        Tick();
                        if (attribute.Name is "ID" or "xml:id" && attribute.Value == address.LeafId) originalIds.Add((element, attribute));
                    }
                }
                var ids = target.IdIndex.GetValueOrDefault(address.LeafId) ?? [];
                // Reject ambiguity before scanning leaf summaries: otherwise repeated raw IDs
                // could multiply the uncharged path comparisons inside that scan.
                if (originalIds.Count > 1 || ids.Count > 1) return CheckStatus.NotEvaluated;
                var originalPath = originalIds.Count == 1 ? originalIds[0].Element.NodePath : null;
                var summaries = new List<ParsedPackageLeaf>();
                foreach (var leaf in target.Leaves)
                {
                    Tick();
                    if (leaf.Id == address.LeafId || leaf.Address?.LeafId == address.LeafId ||
                        originalPath == leaf.NodePath) summaries.Add(leaf);
                }
                // Absence is established only when all three frozen representations agree.
                if (originalIds.Count == 0) return ids.Count == 0 && summaries.Count == 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated;
                if (originalIds.Count != 1 || ids.Count != 1) return CheckStatus.NotEvaluated;
                var original = originalIds[0];
                var isLeaf = original.Element is { Name: "leaf", LocalName: "leaf", NamespaceUri.Length: 0 };
                if (original.Attribute.IsDefault || ids[0].NodePath != original.Element.NodePath || ids[0].IsLeaf != isLeaf ||
                    ids[0].ElementName != original.Element.Name) return CheckStatus.NotEvaluated;
                if (!isLeaf) return summaries.Count == 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated;
                if (original.Attribute is not { Name: "ID", LocalName: "ID", NamespaceUri.Length: 0 } || summaries.Count != 1 ||
                    summaries[0].Id != address.LeafId || summaries[0].Address != address || summaries[0].NodePath != original.Element.NodePath)
                    return CheckStatus.NotEvaluated;
                // A delete leaf is still an existing leaf element. Effectiveness and content
                // file availability are deliberately not used to decide this rule.
                return CheckStatus.Pass;
            }
        }
        catch (ReferencedLeafWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked each STF target leaf against complete, context-bound original elements, ID index and derived leaf facts. Missing history or incomplete/ambiguous backbones remain unevaluated. Lifecycle effectiveness, delete operations and missing physical content do not make an existing leaf disappear.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new ReferencedLeafWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class ReferencedLeafWorkLimitException : Exception;
}
