using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfReferenceCoverageRule
{
    internal const string RuleId = "FDA47-1789";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The study section STF reference inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1789" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1789 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2021, 9, 15))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1789.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured STF specification is required.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        // Absence requires completed discovery, unlike a local positive attribute violation.
        // Unrelated unresolved historical doc-content does not make TargetScopeComplete false.
        if (!bindings.TargetScopeComplete) return Unknown("Incomplete STF discovery or target backbone scope cannot establish absent coverage.");
        var incomplete = false;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var indices = new List<ParsedBackbone>();
            var observations = new Dictionary<(string Digest, string Path), int>();
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                foreach (var stf in xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    var key = (xml.InputDigest, stf.LogicalPath);
                    observations[key] = observations.GetValueOrDefault(key) + 1;
                }
                if (xml.InputDigest != packages.Target.InputDigest || xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                    xml.RulesDigest != context.Catalog.Digest || xml.AssetManifestDigest != context.Assets.Digest ||
                    xml.LimitsDigest != packages.Target.Limits.Digest()) continue;
                foreach (var document in xml.Backbones)
                {
                    Tick();
                    if (document.Kind == BackboneKind.Ich && document.LogicalPath == packages.Target.Manifest.SequenceNumber + "/index.xml") indices.Add(document);
                }
            }
            if (indices.Count != 1 || !indices[0].ReadComplete || indices[0].DtdStatus != CheckStatus.Pass ||
                indices[0].ProfileSnapshotId != lifecycle.ProfileSnapshotId)
                return Unknown("A unique completely read target ICH backbone is required.");
            var index = indices[0];
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in index.Elements) { Tick(); if (!elements.TryAdd(element.NodePath, element)) return Unknown("Target element identities are ambiguous."); }
            var stfPaths = new HashSet<string>(StringComparer.Ordinal);
            var active = new List<(BoundStudyTaggingFile Document, HashSet<string> Headings, bool UnknownOwner)>();
            foreach (var bound in bindings.Documents)
            {
                Tick();
                var relevant = bound.IsTarget;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber == packages.Target.Manifest.SequenceNumber) relevant = true;
                }
                if (!relevant) continue; // Unrelated historical discovery cannot hide current uncovered leaves.
                var roots = new List<ParsedXmlElement>();
                foreach (var element in bound.Document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                if (observations.GetValueOrDefault((bound.InputDigest, bound.Document.LogicalPath)) != 1 || roots.Count != 1 ||
                    roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                    return Unknown("STF identity observations are ambiguous or incomplete.");
                stfPaths.Add(bound.Document.LogicalPath);
                var headings = new HashSet<string>(StringComparer.Ordinal);
                var unknownOwner = false;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    var heading = Heading(owner.Leaf);
                    if (!owner.Qualified || heading is null) { unknownOwner = true; continue; }
                    if (heading.Value.Section.StartsWith("m4.", StringComparison.Ordinal) || heading.Value.Section.StartsWith("m5.", StringComparison.Ordinal))
                        headings.Add(heading.Value.Path);
                    else unknownOwner = true;
                }
                if (headings.Count == 0 && !unknownOwner) continue; // An orphan or historical-only STF is not submitted coverage.
                if (!bound.StructureComplete) return Unknown("An actual submitted STF has unread or unqualified reference structure.");
                active.Add((bound, headings, unknownOwner));
            }
            var candidates = new List<(ParsedPackageLeaf Leaf, string Heading, string ContentPath)>();
            var seen = new HashSet<LeafAddress>();
            foreach (var leaf in index.Leaves)
            {
                Tick();
                if (leaf.Operation == "delete" || leaf.IsRegionalReference) continue;
                var heading = Heading(leaf);
                if (heading is null) { incomplete = true; continue; }
                if (!Required(heading.Value.Section)) continue;
                if (leaf.Address is null || !leaf.ContextComplete || !elements.TryGetValue(leaf.NodePath, out var element) ||
                    leaf.Address.ApplicationId != packages.Target.Manifest.ApplicationId || leaf.Address.SequenceNumber != packages.Target.Manifest.SequenceNumber ||
                    leaf.Address.BackboneRelativePath != "index.xml" || leaf.Address.LeafId != leaf.Id ||
                    element.Location.LogicalPath != index.LogicalPath || element.Location.NodePath != element.NodePath ||
                    leaf.Location.LogicalPath != index.LogicalPath || leaf.Location.NodePath != element.NodePath ||
                    !Plain(element, "leaf") || !index.IdIndex.TryGetValue(leaf.Address.LeafId, out var ids) || ids.Count != 1 || !ids[0].IsLeaf ||
                    ids[0].NodePath != leaf.NodePath || Explicit(element, "ID", "") != leaf.Id ||
                    Explicit(element, "operation", "") is not ("new" or "append" or "replace") ||
                    Explicit(element, "operation", "") != leaf.Operation || Explicit(element, "href", "http://www.w3c.org/1999/xlink") != leaf.Href)
                { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href!); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null)
                { incomplete = true; continue; }
                if (stfPaths.Contains(path)) continue; // Actual STF XML is not required to reference itself.
                var sequence = path.Split('/')[0];
                if (!packages.Sequences.TryGetValue(sequence, out var capture)) { incomplete = true; continue; }
                var files = 0;
                foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files++; }
                if (files != 1) { incomplete = true; continue; }
                if (!seen.Add(leaf.Address)) { incomplete = true; continue; }
                candidates.Add((leaf, heading.Value.Path, path));
            }
            foreach (var candidate in candidates)
            {
                Tick();
                applicable = true;
                var covered = false;
                var uncertain = false;
                foreach (var entry in active)
                {
                    Tick();
                    foreach (var reference in entry.Document.References)
                    {
                        Tick();
                        if (reference.Target != candidate.Leaf.Address) continue;
                        if (reference.Status == StfReferenceStatus.Resolved && entry.Headings.Contains(candidate.Heading)) covered = true;
                        else if (reference.Status == StfReferenceStatus.Resolved || reference.Status is StfReferenceStatus.AmbiguousId or
                            StfReferenceStatus.BackboneIncomplete or StfReferenceStatus.BackboneUnavailable or StfReferenceStatus.BackboneInvalid or
                            StfReferenceStatus.LeafUnqualified) uncertain = true;
                        // A complete invalid/future/other-address reference cannot cover this leaf.
                        // No speculative "after repair" retargeting is used to hide absence.
                    }
                }
                if (covered) continue;
                if (uncertain) { incomplete = true; continue; }
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "This submitted study-section content leaf is not referenced by an appropriately associated submitted STF.",
                    candidate.Leaf.Location with { FieldPath = "STF/doc-content/@xlink:href", Files = [new(packages.Target.SourceId, index.LogicalPath),
                        new(packages.Sequences[candidate.ContentPath.Split('/')[0]].SourceId, candidate.ContentPath)] },
                    Expected: "An exact STF reference to " + candidate.Leaf.Address, Actual: "No covering reference"));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A study content leaf has proven missing STF coverage; other leaves may remain unqualified.", findings.ToArray()));
            }

            (string Path, string Section)? Heading(ParsedPackageLeaf leaf)
            {
                if (!elements.TryGetValue(leaf.NodePath, out var current)) return null;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                ParsedXmlElement? Parent()
                {
                    Tick();
                    if (!visited.Add(current.NodePath) || current.ParentPath is not { } path || !elements.TryGetValue(path, out var parent)) return null;
                    var contains = false;
                    foreach (var child in parent.Children) { Tick(); if (child == current.NodePath) contains = true; }
                    if (!contains) return null;
                    current = parent;
                    return parent;
                }
                var heading = Parent();
                // Standard SectionPath determines applicability, while the nearest actual
                // extension instance remains part of the coverage association identity.
                var associationPath = heading?.NodePath;
                while (heading is not null && Plain(heading, "node-extension")) heading = Parent();
                if (heading is null || heading.NamespaceUri.Length != 0 || !context.NodeSchema.Definitions.TryGetValue(heading.Name, out var definition) ||
                    definition.SectionPath is not { } section) return null;
                var headingPath = associationPath ?? heading.NodePath;
                while (definition.ParentDefinitionKey is { } parentKey)
                {
                    var parent = Parent();
                    if (parent is null || !Plain(parent, parentKey)) return null;
                    definition = context.NodeSchema.Get(parentKey);
                }
                return Parent() is { ParentPath: null, LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } ? (headingPath, section) : null;
            }
        }
        catch (ReferenceCoverageWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked current non-delete content leaves in m4.2 and m5.3 study sections, excluding m5.3.6, by exact leaf address. Coverage requires an actual current STF owner and matching heading/extension instance. Cross-instance or otherwise ambiguous associations remain unqualified; local passes do not establish full FDA 1789 coverage.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new ReferenceCoverageWorkLimitException(); }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            var count = 0; string? value = null;
            foreach (var attribute in element.Attributes.Values)
            {
                Tick();
                if (attribute.LocalName != name || attribute.NamespaceUri != ns) continue;
                count++;
                if (!attribute.IsDefault && attribute.ExplicitValue == attribute.Value) value = attribute.ExplicitValue;
            }
            return count == 1 ? value : null;
        }
    }
    private static bool Required(string section) => Within(section, "m4.2") || Within(section, "m5.3") && !Within(section, "m5.3.6");
    private static bool Within(string section, string parent) => section == parent || section.StartsWith(parent + ".", StringComparison.Ordinal);
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class ReferenceCoverageWorkLimitException : Exception;
}
