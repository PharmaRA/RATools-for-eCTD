using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfTitleMatchRule
{
    internal const string RuleId = "FDA47-1953";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF title comparison did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1953" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1953 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2013, 4, 30))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1953.");
        if (lifecycle.InputDigest != packages.Target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != packages.Target.Limits.Digest())
            return Unknown("The lifecycle observations do not bind the selected inputs and context.");
        var work = packages.Target.Limits.MaxXmlNodes;
        var incomplete = false;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var targetNumber = packages.Target.Manifest.SequenceNumber;
        try
        {
            var xml = Xml(targetNumber);
            if (xml is null) return Unknown("A unique context-bound current XML observation is required.");
            var indices = new List<ParsedBackbone>();
            foreach (var backbone in xml.Backbones) { Tick(); if (backbone.Kind == BackboneKind.Ich && backbone.LogicalPath == targetNumber + "/index.xml") indices.Add(backbone); }
            if (indices.Count != 1 || indices[0].ProfileSnapshotId != xml.ProfileSnapshotId || !indices[0].ReadComplete || indices[0].DtdStatus != CheckStatus.Pass)
                return Unknown("A complete, profile-bound current ICH backbone is required.");
            var index = indices[0];
            var elements = Tree(index.Elements);
            if (elements is null) return Unknown("Current backbone parent and child evidence is contradictory.");
            var roots = new List<ParsedXmlElement>();
            foreach (var element in index.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
            if (roots.Count != 1 || roots[0] is not { Name: "ectd:ectd", LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" })
                return Unknown("A unique legal ICH root is required for current owner selection.");
            var rawIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var leaves = new Dictionary<string, List<ParsedPackageLeaf>>(StringComparer.Ordinal);
            foreach (var element in index.Elements)
            { Tick(); var id = Explicit(element, "ID", ""); if (id is not null) rawIds[id] = rawIds.GetValueOrDefault(id) + 1; }
            foreach (var leaf in index.Leaves)
            { Tick(); if (!leaves.TryGetValue(leaf.NodePath, out var values)) leaves.Add(leaf.NodePath, values = []); values.Add(leaf); }
            foreach (var element in index.Elements)
            {
                Tick();
                if (!Plain(element, "leaf")) continue;
                if (!Connected(element, roots[0], elements)) { incomplete = true; continue; }
                if (!leaves.TryGetValue(element.NodePath, out var values) || values.Count != 1) { incomplete = true; continue; }
                var leaf = values[0];
                var operation = Explicit(element, "operation", "");
                if (operation != leaf.Operation || operation is not ("new" or "append" or "replace" or "delete")) { incomplete = true; continue; }
                if (operation == "delete") continue;
                var id = Explicit(element, "ID", "");
                var href = Explicit(element, "href", "http://www.w3c.org/1999/xlink");
                if (id is null || id != leaf.Id || rawIds.GetValueOrDefault(id) != 1 || href != leaf.Href || leaf.ParentNodePath != element.ParentPath ||
                    leaf.Address is not { } address || address.ApplicationId != packages.Target.Manifest.ApplicationId || address.SequenceNumber != targetNumber ||
                    address.BackboneRelativePath != "index.xml" || address.LeafId != id || !index.IdIndex.TryGetValue(id, out var ids) ||
                    ids.Count != 1 || !ids[0].IsLeaf || ids[0].NodePath != element.NodePath)
                { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, href!); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null) { incomplete = true; continue; }
                var identity = Study(path);
                if (identity is null) { incomplete = true; continue; }
                if (!identity.Value.IsStf) continue;
                var leafTitle = Title(element, elements);
                if (leafTitle is null || leafTitle != leaf.Title) { incomplete = true; continue; }
                applicable = true;
                if (string.Equals(identity.Value.Title, leafTitle, StringComparison.Ordinal)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The STF study title differs from the title of its current referencing leaf.",
                    leaf.Location with { FieldPath = "title/study-identifier/title", Files = [new(packages.Target.SourceId, index.LogicalPath), new(identity.Value.SourceId, path)] },
                    Expected: identity.Value.Title, Actual: leafTitle));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A current STF owner has a title mismatch; other pairs may remain unqualified.", findings.ToArray()));
            }
        }
        catch (TitleWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Compared complete decoded research and current owner titles using Ordinal equality without trimming or case folding. Matching empty text means no mismatch, not complete title quality; document titles and old owners are not substituted.", findings);

        PackageXmlInspection? Xml(string sequence)
        {
            Tick();
            if (!packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var selected = context.ForSequence(sequence);
            string? profile = sequence == targetNumber ? lifecycle.ProfileSnapshotId : null;
            if (profile is null) foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
            var matches = new List<PackageXmlInspection>();
            foreach (var observation in lifecycle.XmlInspections)
            {
                Tick();
                if (observation.InputDigest == capture.InputDigest && observation.ProfileSnapshotId == profile && observation.RulesDigest == selected.Catalog.Digest &&
                    observation.AssetManifestDigest == selected.Assets.Digest && observation.LimitsDigest == capture.Limits.Digest()) matches.Add(observation);
            }
            return matches.Count == 1 ? matches[0] : null;
        }
        (bool IsStf, string? Title, string SourceId)? Study(string path)
        {
            Tick();
            var sequence = path.Split('/')[0];
            if (string.CompareOrdinal(sequence, targetNumber) > 0 || !packages.Sequences.TryGetValue(sequence, out var capture)) return null;
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
                    return (false, null, capture.SourceId);
                if (backbones.Count > 0 || !observation.StudyTaggingFiles.InventoryComplete || path.StartsWith(sequence + "/util/", StringComparison.Ordinal)) return null;
                return (false, null, capture.SourceId);
            }
            if (stfs.Count != 1 || backbones.Count > 0 || stfs[0].CapturedSha256 != files[0].Sha256 || !stfs[0].ReadComplete || stfs[0].DtdStatus != CheckStatus.Pass) return null;
            var tree = Tree(stfs[0].Elements);
            if (tree is null) return null;
            var roots = new List<ParsedXmlElement>();
            foreach (var element in stfs[0].Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
            if (roots.Count != 1 || roots[0] is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" }) return null;
            var identifiers = new List<ParsedXmlElement>();
            foreach (var child in roots[0].Children) { Tick(); if (Plain(tree[child], "study-identifier")) identifiers.Add(tree[child]); }
            var title = identifiers.Count == 1 ? Title(identifiers[0], tree) : null;
            return title is null ? null : (true, title, capture.SourceId);
        }
        bool Connected(ParsedXmlElement leaf, ParsedXmlElement root, IReadOnlyDictionary<string, ParsedXmlElement> tree)
        {
            var current = leaf;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (current.NodePath != root.NodePath)
            {
                Tick();
                if (!seen.Add(current.NodePath) || current.ParentPath is not { } parent || !tree.TryGetValue(parent, out var value)) return false;
                current = value;
            }
            return true;
        }
        Dictionary<string, ParsedXmlElement>? Tree(IReadOnlyList<ParsedXmlElement> observations)
        {
            var tree = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var element in observations)
            {
                Tick();
                if (!tree.TryAdd(element.NodePath, element)) return null;
                if (element.ParentPath is not { } parent) continue;
                if (!reverse.TryGetValue(parent, out var children)) reverse.Add(parent, children = new(StringComparer.Ordinal));
                children.Add(element.NodePath);
            }
            foreach (var element in observations)
            {
                Tick();
                if (element.ParentPath is { } parent && !tree.ContainsKey(parent)) return null;
                var listed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in element.Children)
                { Tick(); if (!listed.Add(child) || !tree.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return null; }
                if (reverse.TryGetValue(element.NodePath, out var actual)) foreach (var child in actual) { Tick(); if (!listed.Contains(child)) return null; }
            }
            return tree;
        }
        string? Title(ParsedXmlElement parent, IReadOnlyDictionary<string, ParsedXmlElement> tree)
        {
            var titles = new List<ParsedXmlElement>();
            foreach (var child in parent.Children) { Tick(); if (Plain(tree[child], "title")) titles.Add(tree[child]); }
            return titles.Count == 1 && titles[0].Children.Count == 0 ? titles[0].Text : null;
        }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null; var count = 0;
            foreach (var attribute in element.Attributes.Values) { Tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault) { count++; value = attribute.Value; } }
            return count == 1 ? value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new TitleWorkLimitException(); }
    }
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class TitleWorkLimitException : Exception;
}
