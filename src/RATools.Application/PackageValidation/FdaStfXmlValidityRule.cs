using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfXmlValidityRule
{
    internal const string RuleId = "FDA47-1969";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF XML validity inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1969" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1969 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1969.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification)) return Unknown("The captured STF specification is required.");
        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var knownCurrent = new HashSet<string>(StringComparer.Ordinal);
        var target = packages.Target.Manifest.SequenceNumber;
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
                    if (owner.BackboneLogicalPath != target + "/index.xml") continue;
                    if (!CurrentOwner(owner)) { if (!bound.IsTarget) incomplete = true; continue; }
                    selected = true;
                }
                if (!selected) continue;
                var document = bound.Document;
                var xmls = new List<PackageXmlInspection>();
                foreach (var xml in lifecycle.XmlInspections) { Tick(); if (xml.InputDigest == bound.InputDigest) xmls.Add(xml); }
                if (xmls.Count != 1) { incomplete = true; continue; }
                var observed = 0;
                foreach (var stf in xmls[0].StudyTaggingFiles.Documents) { Tick(); if (stf.LogicalPath == document.LogicalPath) observed++; }
                if (observed != 1) { incomplete = true; continue; }
                var elements = Tree(document.Elements);
                if (elements is null) { incomplete = true; continue; }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                if (roots.Count != 1 || roots[0] is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                { incomplete = true; continue; }
                applicable = true;
                if (bound.IsTarget) knownCurrent.Add(document.LogicalPath);
                var sequence = document.LogicalPath.Split('/')[0];
                string? invalid = null;
                var uncertainDiagnostic = false;
                foreach (var diagnostic in xmls[0].StudyTaggingFiles.Findings)
                {
                    Tick();
                    if (diagnostic.Location.LogicalPath != document.LogicalPath || diagnostic.Location.SequenceNumber != sequence) continue;
                    if (diagnostic.RuleId == "STF-DTD" && diagnostic.CheckStatus == CheckStatus.Fail &&
                        diagnostic.Code is "XML_NOT_WELL_FORMED" or "DTD_VALIDATION_ERROR") invalid ??= diagnostic.Code;
                    else if (diagnostic.CheckStatus is CheckStatus.Fail or CheckStatus.NotEvaluated) uncertainDiagnostic = true;
                }
                string? studyId = null;
                if (document.ReadComplete)
                {
                    var identifiers = Children(roots[0], "study-identifier", elements);
                    var ids = identifiers.Count == 1 ? Children(identifiers[0], "study-id", elements) : [];
                    if (roots[0].Children.Count == 0) invalid ??= "NO_CHILD_ELEMENTS";
                    else if (identifiers.Count == 0 || identifiers.Count == 1 && ids.Count == 0) invalid ??= "STUDY_ID_ABSENT";
                    else if (ids.Count == 1 && ids[0].Children.Count == 0) studyId = ids[0].Text;
                }
                if (invalid is null)
                {
                    incomplete |= !document.ReadComplete || document.DtdStatus != CheckStatus.Pass || uncertainDiagnostic || string.IsNullOrWhiteSpace(studyId);
                    continue;
                }
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "An identified STF has a definite XML/DTD error or a completely observed missing required structure.",
                    roots[0].Location with { FieldPath = "xml-validity", Files = [new(bound.SourceId, document.LogicalPath)] },
                    Expected: "A loadable STF with required structure", Actual: invalid));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An STF invalidity is established; other load outcomes may remain unqualified.", findings.ToArray()));
            }
            // Keep unknown current references in scope even when no STF can be bound.
            var targetXml = Xml(target);
            if (targetXml is null) incomplete = true;
            else foreach (var backbone in targetXml.Backbones)
            {
                Tick();
                if (backbone.Kind != BackboneKind.Ich || backbone.LogicalPath != target + "/index.xml") continue;
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
                    if (knownCurrent.Contains(path)) continue;
                    if (operation is not ("new" or "append" or "replace")) { incomplete = true; continue; }
                    var sequence = path.Split('/')[0];
                    if (!packages.Sequences.TryGetValue(sequence, out var capture)) { incomplete = true; continue; }
                    var files = new List<PackageInputFile>();
                    foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files.Add(file); }
                    if (files.Count != 1) { incomplete = true; continue; }
                    if (sequence == target) continue;
                    var historical = Xml(sequence);
                    if (historical is null) { incomplete = true; continue; }
                    var documents = new List<ParsedStudyTaggingFile>();
                    foreach (var document in historical.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) documents.Add(document); }
                    if (documents.Count == 0) { incomplete |= !historical.StudyTaggingFiles.InventoryComplete; continue; }
                    if (documents.Count != 1 || documents[0].CapturedSha256 != files[0].Sha256) { incomplete = true; continue; }
                    var qualified = false;
                    foreach (var bound in bindings.Documents)
                    {
                        Tick();
                        if (bound.InputDigest != capture.InputDigest || bound.Document.LogicalPath != path) continue;
                        foreach (var owner in bound.Owners) { Tick(); if (owner.Leaf.NodePath == element.NodePath && CurrentOwner(owner)) qualified = true; }
                    }
                    if (!qualified) incomplete = true;
                }
            }
        }
        catch (ValidityWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked identified STF roots against exact-file error categories and complete structure. Declaration-only identity, discovery failure classification, empty-ID semantics and interrupted observation retention remain unqualified; environment/limit failures are not automatically invalid XML.", findings);

        PackageXmlInspection? Xml(string sequence)
        {
            Tick();
            if (!packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var selected = context.ForSequence(sequence);
            string? profile = sequence == target ? lifecycle.ProfileSnapshotId : null;
            if (profile is null) foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
            var matches = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections)
            { Tick(); if (xml.InputDigest == capture.InputDigest && xml.ProfileSnapshotId == profile && xml.RulesDigest == selected.Catalog.Digest && xml.AssetManifestDigest == selected.Assets.Digest && xml.LimitsDigest == capture.Limits.Digest()) matches.Add(xml); }
            return matches.Count == 1 ? matches[0] : null;
        }
        bool CurrentOwner(StfOwnerBinding owner)
        {
            Tick();
            if (!owner.Qualified || owner.BackboneLogicalPath != target + "/index.xml" || owner.Leaf.Address is not { } address ||
                address.ApplicationId != packages.Target.Manifest.ApplicationId || address.SequenceNumber != target || address.BackboneRelativePath != "index.xml" || address.LeafId != owner.Leaf.Id) return false;
            var xml = Xml(target);
            if (xml is null) return false;
            var indices = new List<ParsedBackbone>();
            foreach (var backbone in xml.Backbones) { Tick(); if (backbone.Kind == BackboneKind.Ich && backbone.LogicalPath == owner.BackboneLogicalPath) indices.Add(backbone); }
            return indices.Count == 1 && indices[0].ProfileSnapshotId == xml.ProfileSnapshotId && indices[0].ReadComplete && indices[0].DtdStatus == CheckStatus.Pass;
        }
        Dictionary<string, ParsedXmlElement>? Tree(IReadOnlyList<ParsedXmlElement> observations)
        {
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var element in observations)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element)) return null;
                if (element.ParentPath is not { } parent) continue;
                if (!reverse.TryGetValue(parent, out var children)) reverse.Add(parent, children = new(StringComparer.Ordinal));
                children.Add(element.NodePath);
            }
            foreach (var element in observations)
            {
                Tick();
                if (element.ParentPath is { } parent && !elements.ContainsKey(parent)) return null;
                var listed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in element.Children) { Tick(); if (!listed.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return null; }
                if (reverse.TryGetValue(element.NodePath, out var actual)) foreach (var child in actual) { Tick(); if (!listed.Contains(child)) return null; }
            }
            var roots = new List<ParsedXmlElement>();
            foreach (var element in observations) { Tick(); if (element.ParentPath is null) roots.Add(element); }
            if (roots.Count != 1) return null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(roots[0].NodePath);
            while (pending.Count > 0)
            {
                Tick();
                var path = pending.Pop();
                if (!visited.Add(path)) return null;
                foreach (var child in elements[path].Children) { Tick(); pending.Push(child); }
            }
            if (visited.Count != elements.Count) return null;
            return elements;
        }
        List<ParsedXmlElement> Children(ParsedXmlElement parent, string name, IReadOnlyDictionary<string, ParsedXmlElement> elements)
        {
            var result = new List<ParsedXmlElement>();
            foreach (var path in parent.Children) { Tick(); if (Plain(elements[path], name)) result.Add(elements[path]); }
            return result;
        }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null; var count = 0;
            foreach (var attribute in element.Attributes.Values) { Tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault) { count++; value = attribute.Value; } }
            return count == 1 ? value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new ValidityWorkLimitException(); }
    }
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class ValidityWorkLimitException : Exception;
}
