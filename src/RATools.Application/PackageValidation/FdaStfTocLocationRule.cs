using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfTocLocationRule
{
    internal const string RuleId = "FDA47-1901";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF TOC location inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1901" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1901 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1901.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured STF specification is required.");
        if (lifecycle.InputDigest != packages.Target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != packages.Target.Limits.Digest())
            return Unknown("The lifecycle facts do not bind the selected inputs and context.");

        var incomplete = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        var targetNumber = packages.Target.Manifest.SequenceNumber;
        try
        {
            var targetXml = Xml(targetNumber);
            if (targetXml is null) return Unknown("A unique context-bound target XML observation is required.");
            var indices = new List<ParsedBackbone>();
            foreach (var backbone in targetXml.Backbones) { Tick(); if (backbone.Kind == BackboneKind.Ich && backbone.LogicalPath == targetNumber + "/index.xml") indices.Add(backbone); }
            if (indices.Count != 1 || indices[0].ProfileSnapshotId != targetXml.ProfileSnapshotId || !indices[0].ReadComplete || indices[0].DtdStatus != CheckStatus.Pass)
                return Unknown("A unique complete target ICH backbone is required for TOC placement.");
            foreach (var backbone in targetXml.Backbones)
            {
                Tick();
                if (backbone.ProfileSnapshotId != targetXml.ProfileSnapshotId || !backbone.ReadComplete || backbone.DtdStatus != CheckStatus.Pass) { incomplete = true; continue; }
                if (backbone.Kind == BackboneKind.Ich && backbone != indices[0]) { incomplete = true; continue; }
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                var rawIds = new Dictionary<string, int>(StringComparer.Ordinal);
                var derived = new Dictionary<string, List<ParsedPackageLeaf>>(StringComparer.Ordinal);
                var unique = true;
                foreach (var element in backbone.Elements)
                {
                    Tick();
                    unique &= elements.TryAdd(element.NodePath, element);
                    var id = Explicit(element, "ID", "");
                    if (id is not null) rawIds[id] = rawIds.GetValueOrDefault(id) + 1;
                }
                foreach (var leaf in backbone.Leaves)
                {
                    Tick();
                    if (!derived.TryGetValue(leaf.NodePath, out var entries)) derived.Add(leaf.NodePath, entries = []);
                    entries.Add(leaf);
                }
                if (!unique) { incomplete = true; continue; }
                foreach (var element in backbone.Elements)
                {
                    Tick();
                    if (!Plain(element, "leaf")) continue;
                    if (!derived.TryGetValue(element.NodePath, out var leaves) || leaves.Count != 1) { incomplete = true; continue; }
                    var leaf = leaves[0];
                    var operation = Explicit(element, "operation", "");
                    if (operation != leaf.Operation || operation is not ("new" or "append" or "replace" or "delete")) { incomplete = true; continue; }
                    if (operation == "delete") continue;
                    var href = Explicit(element, "href", "http://www.w3c.org/1999/xlink");
                    var id = Explicit(element, "ID", "");
                    if (href != leaf.Href || id is null || id != leaf.Id || rawIds.GetValueOrDefault(id) != 1 ||
                        leaf.ParentNodePath != element.ParentPath || leaf.Address is not { } address ||
                        address.ApplicationId != packages.Target.Manifest.ApplicationId || address.SequenceNumber != targetNumber ||
                        address.BackboneRelativePath != backbone.LogicalPath[(targetNumber.Length + 1)..] || address.LeafId != id ||
                        !backbone.IdIndex.TryGetValue(id, out var ids) || ids.Count != 1 || !ids[0].IsLeaf || ids[0].NodePath != element.NodePath)
                    { incomplete = true; continue; }
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(backbone.LogicalPath, href!); }
                    catch (ArgumentException) { incomplete = true; continue; }
                    if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null)
                    { incomplete = true; continue; }
                    var identity = Identity(path);
                    if (identity is null) { incomplete = true; continue; }
                    if (!identity.Value.Stf) continue;
                    var section = backbone.Kind == BackboneKind.Ich ? Section(element, elements) :
                        RegionalModule(backbone, element, leaf, elements);
                    if (section is null) { incomplete = true; continue; }
                    if (Within(section, "m4") || Within(section, "m5"))
                    { incomplete = true; continue; } // Module membership alone does not qualify a heading.
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "A current leaf places an identified STF outside modules 4 and 5.",
                        leaf.Location with { FieldPath = "toc-location", Files = [new(packages.Target.SourceId, backbone.LogicalPath), new(identity.Value.SourceId, path)] },
                        Expected: "A qualified STF heading in module 4 or 5", Actual: section));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An STF module violation is established; other headings may remain unqualified.", findings.ToArray()));
                }
            }
        }
        catch (LocationWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.NotApplicable,
            "Checked exact current STF owners for module-external placement, including actual STF content selected by a qualified US regional module-one leaf. Specific allowed headings within modules 4/5 and STF content misread as a regional backbone remain unqualified; no complete location Pass is asserted.", findings);

        string? RegionalModule(ParsedBackbone backbone, ParsedXmlElement leafElement, ParsedPackageLeaf leaf,
            IReadOnlyDictionary<string, ParsedXmlElement> elements)
        {
            Tick();
            if (backbone.Kind != BackboneKind.UsRegional || !backbone.LogicalPath.StartsWith(targetNumber + "/", StringComparison.Ordinal) ||
                leaf.Location.LogicalPath != backbone.LogicalPath || leaf.Location.NodePath != leafElement.NodePath) return null;
            var files = 0;
            foreach (var file in packages.Target.Manifest.Files) { Tick(); if (file.LogicalPath == backbone.LogicalPath) files++; }
            var xml = Xml(targetNumber);
            if (files != 1 || xml is null) return null;
            var observations = 0;
            foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == backbone.LogicalPath) observations++; }
            if (observations != 1) return null;
            if (Raw("ID", "") != leaf.Id || Raw("operation", "") != leaf.Operation ||
                Raw("href", "http://www.w3c.org/1999/xlink") is not { } href || href != leaf.Href) return null;
            var roots = new List<ParsedXmlElement>();
            var parents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var element in elements.Values)
            {
                Tick();
                if (element.ParentPath is null) roots.Add(element);
                foreach (var child in element.Children)
                {
                    Tick();
                    if (!parents.TryGetValue(child, out var owners)) parents.Add(child, owners = []);
                    owners.Add(element.NodePath);
                }
            }
            if (roots.Count != 1 || roots[0] is not { LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" }) return null;
            var current = leafElement;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var moduleOne = false;
            while (true)
            {
                Tick();
                if (!seen.Add(current.NodePath) || current.Location.LogicalPath != backbone.LogicalPath || current.Location.NodePath != current.NodePath) return null;
                if (current.ParentPath is not { } path)
                    return current == roots[0] && moduleOne && !parents.ContainsKey(current.NodePath) ? "m1" : null;
                if (!elements.TryGetValue(path, out var parent) || !parents.TryGetValue(current.NodePath, out var owners) ||
                    owners.Count != 1 || owners[0] != parent.NodePath) return null;
                if (Plain(current, "m1-regional"))
                {
                    if (parent != roots[0] || moduleOne) return null;
                    moduleOne = true;
                }
                current = parent;
            }

            string? Raw(string name, string ns)
            {
                var count = 0; string? value = null;
                foreach (var attribute in leafElement.Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName != name || attribute.NamespaceUri != ns) continue;
                    count++;
                    if (!attribute.IsDefault && attribute.Value == attribute.ExplicitValue) value = attribute.ExplicitValue;
                }
                return count == 1 ? value : null;
            }
        }

        PackageXmlInspection? Xml(string sequence)
        {
            Tick();
            if (!packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var selected = context.ForSequence(sequence);
            string? profile = sequence == targetNumber ? lifecycle.ProfileSnapshotId : null;
            if (profile is null) foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
            var matches = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                if (xml.InputDigest == capture.InputDigest && xml.ProfileSnapshotId == profile && xml.RulesDigest == selected.Catalog.Digest &&
                    xml.AssetManifestDigest == selected.Assets.Digest && xml.LimitsDigest == capture.Limits.Digest()) matches.Add(xml);
            }
            return matches.Count == 1 ? matches[0] : null;
        }
        (bool Stf, string SourceId)? Identity(string path)
        {
            Tick();
            var sequence = path.Split('/')[0];
            if (string.CompareOrdinal(sequence, targetNumber) > 0 || !packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var files = new List<PackageInputFile>();
            foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files.Add(file); }
            if (files.Count != 1) return null;
            var xml = Xml(sequence);
            if (xml is null) return null;
            var stfs = new List<ParsedStudyTaggingFile>();
            var backbones = new List<ParsedBackbone>();
            foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) stfs.Add(document); }
            foreach (var backbone in xml.Backbones) { Tick(); if (backbone.LogicalPath == path) backbones.Add(backbone); }
            if (stfs.Count == 0)
            {
                if (backbones.Count == 1 && backbones[0].ProfileSnapshotId == xml.ProfileSnapshotId && backbones[0].ReadComplete && backbones[0].DtdStatus == CheckStatus.Pass) return (false, capture.SourceId);
                if (backbones.Count > 0 || path.StartsWith(sequence + "/util/", StringComparison.Ordinal) || !xml.StudyTaggingFiles.InventoryComplete) return null;
                return (false, capture.SourceId);
            }
            if (stfs.Count != 1 || backbones.Count > 0 || stfs[0].CapturedSha256 != files[0].Sha256) return null;
            var roots = new List<ParsedXmlElement>();
            foreach (var element in stfs[0].Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
            return roots.Count == 1 && roots[0] is { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" } ? (true, capture.SourceId) : null;
        }
        string? Section(ParsedXmlElement leaf, IReadOnlyDictionary<string, ParsedXmlElement> elements)
        {
            var current = leaf;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ParsedXmlElement? Parent()
            {
                Tick();
                if (!seen.Add(current.NodePath) || current.ParentPath is not { } path || !elements.TryGetValue(path, out var parent)) return null;
                var occurrences = 0;
                foreach (var child in parent.Children) { Tick(); if (child == current.NodePath) occurrences++; }
                if (occurrences != 1) return null;
                current = parent;
                return parent;
            }
            var heading = Parent();
            while (heading is not null && Plain(heading, "node-extension")) heading = Parent();
            if (heading is null || heading.NamespaceUri.Length != 0 || heading.Name != heading.LocalName ||
                !context.NodeSchema.Definitions.TryGetValue(heading.Name, out var definition) || definition.SectionPath is not { } section) return null;
            while (definition.ParentDefinitionKey is { } parentKey)
            {
                var parent = Parent();
                if (parent is null || !Plain(parent, parentKey)) return null;
                definition = context.NodeSchema.Get(parentKey);
            }
            return Parent() is { ParentPath: null, LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } ? section : null;
        }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null;
            var count = 0;
            foreach (var attribute in element.Attributes.Values)
            { Tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault) { count++; value = attribute.Value; } }
            return count == 1 ? value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new LocationWorkLimitException(); }
    }
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static bool Within(string section, string module) => section == module || section.StartsWith(module + ".", StringComparison.Ordinal);
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class LocationWorkLimitException : Exception;
}
