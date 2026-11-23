using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfMultiSectionReferenceRule
{
    internal const string RuleId = "FDA47-1990";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF cross-section reference inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1990" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1990 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2013, 4, 30))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1990.");
        if (lifecycle.InputDigest != packages.Target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != packages.Target.Limits.Digest())
            return Unknown("The lifecycle facts do not bind the selected inputs and context.");
        var incomplete = false;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var groups = new Dictionary<(string Digest, string Path), Dictionary<string, string>>();
        var reported = new HashSet<(string Digest, string Path)>();
        var work = packages.Target.Limits.MaxXmlNodes;
        var target = packages.Target.Manifest.SequenceNumber;
        try
        {
            var xml = Xml(target);
            if (xml is null) return Unknown("A unique context-bound current XML observation is required.");
            var indices = new List<ParsedBackbone>();
            foreach (var backbone in xml.Backbones) { Tick(); if (backbone.Kind == BackboneKind.Ich && backbone.LogicalPath == target + "/index.xml") indices.Add(backbone); }
            if (indices.Count != 1 || indices[0].ProfileSnapshotId != xml.ProfileSnapshotId || !indices[0].ReadComplete || indices[0].DtdStatus != CheckStatus.Pass)
                return Unknown("A complete profile-bound current ICH backbone is required.");
            var index = indices[0];
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var leaves = new Dictionary<string, List<ParsedPackageLeaf>>(StringComparer.Ordinal);
            var rawIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var roots = new List<ParsedXmlElement>();
            foreach (var element in index.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element)) return Unknown("Backbone node identities are ambiguous.");
                if (element.ParentPath is null) roots.Add(element);
                var id = Explicit(element, "ID", "");
                if (id is not null) rawIds[id] = rawIds.GetValueOrDefault(id) + 1;
            }
            if (roots.Count != 1 || roots[0] is not { Name: "ectd:ectd", LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" })
                return Unknown("A unique ICH root is required.");
            foreach (var leaf in index.Leaves)
            { Tick(); if (!leaves.TryGetValue(leaf.NodePath, out var entries)) leaves.Add(leaf.NodePath, entries = []); entries.Add(leaf); }
            foreach (var element in index.Elements)
            {
                Tick();
                if (!Plain(element, "leaf")) continue;
                if (!leaves.TryGetValue(element.NodePath, out var entries) || entries.Count != 1) { incomplete = true; continue; }
                var leaf = entries[0];
                var operation = Explicit(element, "operation", "");
                if (operation != leaf.Operation || operation is not ("new" or "append" or "replace" or "delete")) { incomplete = true; continue; }
                if (operation == "delete") continue;
                var id = Explicit(element, "ID", "");
                var href = Explicit(element, "href", "http://www.w3c.org/1999/xlink");
                if (id is null || id != leaf.Id || rawIds.GetValueOrDefault(id) != 1 || href != leaf.Href || leaf.ParentNodePath != element.ParentPath ||
                    leaf.Address is not { } address || address.ApplicationId != packages.Target.Manifest.ApplicationId || address.SequenceNumber != target ||
                    address.BackboneRelativePath != "index.xml" || address.LeafId != id || !index.IdIndex.TryGetValue(id, out var ids) ||
                    ids.Count != 1 || !ids[0].IsLeaf || ids[0].NodePath != element.NodePath)
                { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, href!); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null) { incomplete = true; continue; }
                var identity = Identity(path);
                if (identity is null) { incomplete = true; continue; }
                if (!identity.Value.Stf) continue;
                var heading = Heading(element, elements, roots[0]);
                if (heading is null) { incomplete = true; continue; }
                applicable = true;
                incomplete |= !identity.Value.Complete;
                var key = (identity.Value.Digest, path);
                if (!groups.TryGetValue(key, out var sections)) groups.Add(key, sections = new(StringComparer.Ordinal));
                if (sections.TryGetValue(heading.Value.Section, out var instance))
                {
                    if (instance != heading.Value.Instance) incomplete = true;
                }
                else sections.Add(heading.Value.Section, heading.Value.Instance);
                if (sections.Count <= 1 || reported.Contains(key)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                reported.Add(key);
                var names = new List<string>();
                foreach (var section in sections.Keys) { Tick(); names.Add(section); }
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The same identified STF is referenced by current leaves in different standard sections.",
                    leaf.Location with { FieldPath = "stf-section-references", Files = [new(packages.Target.SourceId, index.LogicalPath), new(identity.Value.SourceId, path)] },
                    Expected: "One qualified section per STF file", Actual: string.Join(", ", names.Order(StringComparer.Ordinal))));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "Cross-section reuse of an STF is established; other references may remain unqualified.", findings.ToArray()));
            }
        }
        catch (SectionReferenceWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Compared exact STF paths and captured identities across current standard sections. Same-section different instances and full Q&A60 section semantics remain unqualified; matching hashes or Study IDs do not merge different files.", findings);

        PackageXmlInspection? Xml(string sequence)
        {
            Tick();
            if (!packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var selected = context.ForSequence(sequence);
            string? profile = sequence == target ? lifecycle.ProfileSnapshotId : null;
            if (profile is null) foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
            var matches = new List<PackageXmlInspection>();
            foreach (var observation in lifecycle.XmlInspections)
            { Tick(); if (observation.InputDigest == capture.InputDigest && observation.ProfileSnapshotId == profile && observation.RulesDigest == selected.Catalog.Digest && observation.AssetManifestDigest == selected.Assets.Digest && observation.LimitsDigest == capture.Limits.Digest()) matches.Add(observation); }
            return matches.Count == 1 ? matches[0] : null;
        }
        (bool Stf, bool Complete, string Digest, string SourceId)? Identity(string path)
        {
            Tick();
            var sequence = path.Split('/')[0];
            if (string.CompareOrdinal(sequence, target) > 0 || !packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var files = new List<PackageInputFile>();
            foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files.Add(file); }
            if (files.Count != 1) return null;
            var observation = Xml(sequence);
            if (observation is null) return null;
            var stfs = new List<ParsedStudyTaggingFile>();
            var backbones = new List<ParsedBackbone>();
            foreach (var document in observation.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) stfs.Add(document); }
            foreach (var backbone in observation.Backbones) { Tick(); if (backbone.LogicalPath == path) backbones.Add(backbone); }
            if (stfs.Count == 0)
            {
                if (backbones.Count == 1 && backbones[0].ProfileSnapshotId == observation.ProfileSnapshotId && backbones[0].ReadComplete && backbones[0].DtdStatus == CheckStatus.Pass)
                    return (false, true, capture.InputDigest, capture.SourceId);
                if (backbones.Count > 0 || !observation.StudyTaggingFiles.InventoryComplete || path.StartsWith(sequence + "/util/", StringComparison.Ordinal)) return null;
                return (false, true, capture.InputDigest, capture.SourceId);
            }
            if (stfs.Count != 1 || backbones.Count > 0 || stfs[0].CapturedSha256 != files[0].Sha256) return null;
            var roots = new List<ParsedXmlElement>();
            foreach (var element in stfs[0].Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
            return roots.Count == 1 && roots[0] is { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" }
                ? (true, stfs[0].ReadComplete && stfs[0].DtdStatus == CheckStatus.Pass, capture.InputDigest, capture.SourceId) : null;
        }
        (string Section, string Instance)? Heading(ParsedXmlElement leaf, IReadOnlyDictionary<string, ParsedXmlElement> elements, ParsedXmlElement root)
        {
            var current = leaf;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            ParsedXmlElement? Parent()
            {
                Tick();
                if (!visited.Add(current.NodePath) || current.ParentPath is not { } path || !elements.TryGetValue(path, out var parent)) return null;
                var count = 0;
                foreach (var child in parent.Children) { Tick(); if (child == current.NodePath) count++; }
                if (count != 1) return null;
                current = parent;
                return parent;
            }
            var heading = Parent();
            var instance = heading?.NodePath;
            while (heading is not null && Plain(heading, "node-extension")) heading = Parent();
            if (heading is null || heading.NamespaceUri.Length != 0 || heading.Name != heading.LocalName ||
                !context.NodeSchema.Definitions.TryGetValue(heading.Name, out var definition) || definition.SectionPath is not { } section) return null;
            while (definition.ParentDefinitionKey is { } parentKey)
            {
                var parent = Parent();
                if (parent is null || !Plain(parent, parentKey)) return null;
                definition = context.NodeSchema.Get(parentKey);
            }
            return Parent()?.NodePath == root.NodePath ? (section, instance!) : null;
        }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null; var count = 0;
            foreach (var attribute in element.Attributes.Values) { Tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault) { count++; value = attribute.Value; } }
            return count == 1 ? value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new SectionReferenceWorkLimitException(); }
    }
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class SectionReferenceWorkLimitException : Exception;
}
