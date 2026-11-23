using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfDocContentFileTagRule
{
    internal const string RuleId = "FDA47-1918";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF doc-content file-tag inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1918" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1918 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1918.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification)) return Unknown("The captured STF specification is required.");
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var knownCurrentPaths = new HashSet<string>(StringComparer.Ordinal);
        var targetNumber = packages.Target.Manifest.SequenceNumber;
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            foreach (var bound in bindings.Documents)
            {
                Tick();
                var selected = bound.IsTarget;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != targetNumber) continue;
                    if (!owner.Qualified) { if (!bound.IsTarget) incomplete = true; continue; }
                    selected = true;
                }
                if (!selected) continue;
                var document = bound.Document;
                var observations = 0;
                foreach (var xml in lifecycle.XmlInspections)
                {
                    Tick();
                    if (xml.InputDigest != bound.InputDigest) continue;
                    foreach (var observed in xml.StudyTaggingFiles.Documents) { Tick(); if (observed.LogicalPath == document.LogicalPath) observations++; }
                }
                if (observations != 1 || !document.ReadComplete || document.DtdStatus != CheckStatus.Pass)
                { incomplete = true; continue; }
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                var roots = new List<ParsedXmlElement>();
                var treeComplete = true;
                foreach (var element in document.Elements)
                {
                    Tick();
                    treeComplete &= elements.TryAdd(element.NodePath, element);
                    if (element.ParentPath is null) roots.Add(element);
                    else
                    {
                        if (!reverse.TryGetValue(element.ParentPath, out var children)) reverse.Add(element.ParentPath, children = new(StringComparer.Ordinal));
                        children.Add(element.NodePath);
                    }
                }
                if (roots.Count != 1 || roots[0] is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                { incomplete = true; continue; }
                foreach (var element in document.Elements)
                {
                    Tick();
                    if (element.ParentPath is { } parent && !elements.ContainsKey(parent)) treeComplete = false;
                    var listed = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var path in element.Children)
                    {
                        Tick();
                        if (!listed.Add(path) || !elements.TryGetValue(path, out var child) || child.ParentPath != element.NodePath) treeComplete = false;
                    }
                    if (reverse.TryGetValue(element.NodePath, out var actual))
                        foreach (var path in actual) { Tick(); if (!listed.Contains(path)) treeComplete = false; }
                }
                if (!treeComplete) { incomplete = true; continue; }
                if (bound.IsTarget) knownCurrentPaths.Add(document.LogicalPath);
                foreach (var doc in document.Elements)
                {
                    Tick();
                    if (!Plain(doc, "doc-content")) continue;
                    var ancestors = new List<ParsedXmlElement>();
                    var visited = new HashSet<string>(StringComparer.Ordinal);
                    var current = doc;
                    var qualified = false;
                    while (current.ParentPath is { } path)
                    {
                        Tick();
                        if (!visited.Add(current.NodePath) || !elements.TryGetValue(path, out var parent)) break;
                        if (Plain(parent, "content-block")) { ancestors.Add(parent); current = parent; continue; }
                        qualified = Plain(parent, "study-document") && parent.ParentPath == roots[0].NodePath;
                        break;
                    }
                    if (!qualified) { incomplete = true; continue; }
                    applicable = true;
                    if (HasTag(doc)) continue;
                    var inherited = false;
                    foreach (var ancestor in ancestors) { Tick(); inherited |= HasTag(ancestor); }
                    // The pinned sources do not qualify ancestor-tag inheritance.
                    if (inherited) { incomplete = true; continue; }
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "A completely read STF doc-content omits a file-tag and has no ancestor block tag requiring inheritance qualification.",
                        doc.Location with { FieldPath = "file-tag", Files = [new(bound.SourceId, document.LogicalPath)] },
                        Expected: "A doc-content file-tag", Actual: "Absent"));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A doc-content tag omission is established; other references may remain unqualified.", findings.ToArray()));
                }
                bool HasTag(ParsedXmlElement parent)
                {
                    var present = false;
                    foreach (var path in parent.Children) { Tick(); present |= Plain(elements[path], "file-tag"); }
                    return present;
                }
            }
            // Raw current owners retain missing/contradictory historical scope even
            // when the generic reader cannot bind a non-XML extension or derived delete.
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                if (xml.InputDigest != packages.Target.InputDigest || xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                    xml.RulesDigest != context.Catalog.Digest || xml.AssetManifestDigest != context.Assets.Digest || xml.LimitsDigest != packages.Target.Limits.Digest()) continue;
                foreach (var backbone in xml.Backbones)
                {
                    Tick();
                    if (backbone.Kind != BackboneKind.Ich || backbone.LogicalPath != targetNumber + "/index.xml") continue;
                    foreach (var element in backbone.Elements)
                    {
                        Tick();
                        if (!Plain(element, "leaf")) continue;
                        var operation = Explicit(element, "operation", "");
                        if (operation == "delete") continue;
                        PackageReference reference;
                        try { reference = PackageLogicalPath.ResolveReference(backbone.LogicalPath, Explicit(element, "href", "http://www.w3c.org/1999/xlink")!); }
                        catch (ArgumentException) { incomplete = true; continue; }
                        if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null) { incomplete = true; continue; }
                        if (knownCurrentPaths.Contains(path)) continue;
                        if (operation is not ("new" or "append" or "replace")) { incomplete = true; continue; }
                        var sequence = path.Split('/')[0];
                        if (!packages.Sequences.TryGetValue(sequence, out var capture)) { incomplete = true; continue; }
                        var files = new List<PackageInputFile>();
                        foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files.Add(file); }
                        if (files.Count != 1) { incomplete = true; continue; }
                        if (sequence == targetNumber) continue;
                        var selectedContext = context.ForSequence(sequence);
                        string? profile = null;
                        foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
                        var observations = new List<PackageXmlInspection>();
                        foreach (var historical in lifecycle.XmlInspections)
                        {
                            Tick();
                            if (historical.InputDigest == capture.InputDigest && historical.ProfileSnapshotId == profile && historical.RulesDigest == selectedContext.Catalog.Digest &&
                                historical.AssetManifestDigest == selectedContext.Assets.Digest && historical.LimitsDigest == capture.Limits.Digest()) observations.Add(historical);
                        }
                        if (observations.Count != 1) { incomplete = true; continue; }
                        var observed = new List<ParsedStudyTaggingFile>();
                        foreach (var document in observations[0].StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) observed.Add(document); }
                        if (observed.Count == 0) { incomplete |= !observations[0].StudyTaggingFiles.InventoryComplete; continue; }
                        if (observed.Count != 1 || observed[0].CapturedSha256 != files[0].Sha256) { incomplete = true; continue; }
                        var qualified = false;
                        foreach (var bound in bindings.Documents)
                        {
                            Tick();
                            if (bound.InputDigest != capture.InputDigest || bound.Document.LogicalPath != path) continue;
                            foreach (var owner in bound.Owners)
                            { Tick(); if (owner.BackboneLogicalPath == backbone.LogicalPath && owner.Leaf.NodePath == element.NodePath && owner.Qualified) qualified = true; }
                        }
                        if (!qualified) incomplete = true;
                    }
                }
            }
        }
        catch (FileTagWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked direct file-tag presence per complete doc-content occurrence. Ancestor content-block tag inheritance remains unqualified; tag values and multiple tags are separate checks.", findings);

        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null; var count = 0;
            foreach (var attribute in element.Attributes.Values) { Tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault) { count++; value = attribute.Value; } }
            return count == 1 ? value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new FileTagWorkLimitException(); }
    }
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class FileTagWorkLimitException : Exception;
}
