using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfPropertyOrderRule
{
    internal const string RuleId = "FDA47-3015";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF sibling property order inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "3015" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return Unknown("Captured FDA criteria and an explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10)) return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 3015.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification)) return Unknown("The captured STF specification is required.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest()) return Unknown("The lifecycle facts are not bound to this input.");
        var findings = new List<ValidationFinding>();
        var incomplete = false; var applicable = false;
        var work = target.Limits.MaxXmlNodes;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var current = Xml(target.Manifest.SequenceNumber);
            if (current is null) return Unknown("A unique bound current XML inspection is required.");
            incomplete = !current.StudyTaggingFiles.InventoryComplete || !CheckPassed(current, "XML-LIMITS") || !CheckPassed(current, "XML-OFFLINE");
            foreach (var document in current.StudyTaggingFiles.Documents) { Tick(); InspectDocument(target, current, document); }
            var indices = 0;
            foreach (var backbone in current.Backbones) { Tick(); if (backbone.LogicalPath == target.Manifest.SequenceNumber + "/index.xml") indices++; }
            foreach (var backbone in current.Backbones)
            {
                Tick();
                var tree = Tree(backbone.Elements, backbone.LogicalPath, target.Manifest.SequenceNumber);
                var indexQualified = indices == 1 && backbone.Kind == BackboneKind.Ich && backbone.LogicalPath == target.Manifest.SequenceNumber + "/index.xml" &&
                    backbone.ProfileSnapshotId == current.ProfileSnapshotId && backbone.ReadComplete && backbone.DtdStatus == CheckStatus.Pass && tree is not null &&
                    tree.Root is { Name: "ectd:ectd", LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } && tree.Root.Attribute("dtd-version") == "3.2";
                if (!backbone.ReadComplete || backbone.ProfileSnapshotId != current.ProfileSnapshotId || tree is null) incomplete = true;
                if (backbone.Kind == BackboneKind.Ich && !indexQualified) incomplete = true;
                foreach (var element in backbone.Elements)
                {
                    Tick();
                    if (!Plain(element, "leaf")) continue;
                    var operation = Explicit(element, "operation", "");
                    var leaves = new List<ParsedPackageLeaf>();
                    foreach (var leaf in backbone.Leaves) { Tick(); if (leaf.NodePath == element.NodePath) leaves.Add(leaf); }
                    if (operation == "delete")
                    {
                        if (leaves.Count != 1 || leaves[0].Operation != operation) incomplete = true;
                        continue;
                    }
                    var href = Explicit(element, "href", "http://www.w3c.org/1999/xlink");
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(backbone.LogicalPath, href!); }
                    catch (ArgumentException) { incomplete = true; continue; }
                    if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null) { incomplete = true; continue; }
                    var sequence = path.Split('/')[0];
                    if (sequence == target.Manifest.SequenceNumber)
                    {
                        if (File(target, path) is null) incomplete = true;
                        continue;
                    }
                    if (operation is not ("new" or "append" or "replace") || !indexQualified || leaves.Count != 1 || leaves[0].IsRegionalReference ||
                        leaves[0].Operation != operation || leaves[0].Href != href || leaves[0].Address is not { } address ||
                        address.ApplicationId != target.Manifest.ApplicationId || address.SequenceNumber != target.Manifest.SequenceNumber || address.BackboneRelativePath != "index.xml" ||
                        address.LeafId != leaves[0].Id || Explicit(element, "ID", "") != address.LeafId || !OwnerHeading(element, tree!))
                    { incomplete = true; continue; }
                    if (!packages.Sequences.TryGetValue(sequence, out var capture) || string.CompareOrdinal(sequence, target.Manifest.SequenceNumber) >= 0)
                    { incomplete = true; continue; }
                    var historical = Xml(sequence);
                    if (historical is null || File(capture, path) is null) { incomplete = true; continue; }
                    var documents = new List<ParsedStudyTaggingFile>();
                    foreach (var document in historical.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) documents.Add(document); }
                    if (documents.Count == 0) { incomplete |= !historical.StudyTaggingFiles.InventoryComplete; continue; }
                    if (documents.Count != 1) { incomplete = true; continue; }
                    InspectDocument(capture, historical, documents[0]);
                }
            }
            if (indices != 1) incomplete = true;
        }
        catch (OrderWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked direct sibling order within each doc-content/content-block from consistent complete frozen XML. DTD failure alone is not an ordering violation; nested properties and separate containers are not combined.", findings);

        void InspectDocument(ICapturedPackageInput capture, PackageXmlInspection xml, ParsedStudyTaggingFile document)
        {
            Tick();
            if (!seen.Add(capture.InputDigest + ":" + document.LogicalPath)) return;
            var count = 0;
            foreach (var other in xml.StudyTaggingFiles.Documents) { Tick(); if (other.LogicalPath == document.LogicalPath) count++; }
            foreach (var other in xml.Backbones) { Tick(); if (other.LogicalPath == document.LogicalPath) count++; }
            var file = File(capture, document.LogicalPath);
            var selected = context.ForSequence(capture.Manifest.SequenceNumber);
            if (count != 1 || file is null || file.Sha256 != document.CapturedSha256 || !document.ReadComplete ||
                document.DtdStatus is not (CheckStatus.Pass or CheckStatus.Fail) || !selected.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) ||
                !selected.Assets.Assets.TryGetValue("stf-dtd", out var dtd) || dtd.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f" || !Contains(document.ResolvedAssets, "stf-dtd"))
            { incomplete = true; return; }
            var tree = Tree(document.Elements, document.LogicalPath, capture.Manifest.SequenceNumber);
            if (tree is null || tree.Root is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" } || tree.Root.Attribute("dtd-version") != "2.2")
            { incomplete = true; return; }
            var violations = new List<(ParsedXmlElement Container, ParsedXmlElement Tag, ParsedXmlElement Property)>();
            foreach (var container in document.Elements)
            {
                Tick();
                if (!Plain(container, "doc-content") && !Plain(container, "content-block")) continue;
                var current = container;
                while (current.ParentPath is { } parentPath && Plain(tree.Elements[parentPath], "content-block")) { Tick(); current = tree.Elements[parentPath]; }
                if (current.ParentPath is not { } studyPath || !Plain(tree.Elements[studyPath], "study-document") || tree.Elements[studyPath].ParentPath != tree.Root.NodePath)
                { incomplete = true; continue; }
                applicable = true;
                ParsedXmlElement? tag = null;
                foreach (var path in container.Children)
                {
                    Tick(); var child = tree.Elements[path];
                    if (Plain(child, "file-tag")) tag ??= child;
                    else if (Plain(child, "property") && tag is not null) { violations.Add((container, tag, child)); break; }
                }
            }
            // Complete fixed-DTD parsing cannot both accept and observe a forbidden order.
            if (document.DtdStatus == CheckStatus.Pass && violations.Count > 0) { incomplete = true; return; }
            if (document.DtdStatus != CheckStatus.Pass) incomplete = true;
            foreach (var (container, tag, property) in violations)
            {
                Tick();
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "An STF property follows a sibling file-tag in the same container.",
                    property.Location with { FieldPath = "property-order", Files = [new(capture.SourceId, document.LogicalPath)] },
                    "All direct property elements precede direct file-tag elements", $"{container.NodePath}: {tag.NodePath} precedes {property.NodePath}"));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A complete sibling-order violation is established; other files may remain unknown.", findings.ToArray()));
            }
        }
        PackageXmlInspection? Xml(string sequence)
        {
            Tick();
            if (!packages.Sequences.TryGetValue(sequence, out var capture)) return null;
            var selected = context.ForSequence(sequence);
            string? profile = sequence == target.Manifest.SequenceNumber ? lifecycle.ProfileSnapshotId : null;
            if (profile is null) foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
            var matches = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections) { Tick(); if (xml.InputDigest == capture.InputDigest) matches.Add(xml); }
            return matches.Count == 1 && matches[0].ProfileSnapshotId == profile && matches[0].RulesDigest == selected.Catalog.Digest &&
                matches[0].AssetManifestDigest == selected.Assets.Digest && matches[0].LimitsDigest == capture.Limits.Digest() ? matches[0] : null;
        }
        PackageInputFile? File(ICapturedPackageInput capture, string path)
        {
            Tick(); if (!path.StartsWith(capture.Manifest.SequenceNumber + "/", StringComparison.Ordinal)) return null;
            PackageInputFile? found = null;
            foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath != path) continue; if (found is not null) return null; found = file; }
            return found;
        }
        ElementTree? Tree(IReadOnlyList<ParsedXmlElement> observations, string path, string sequence)
        {
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var order = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var element in observations)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != path || element.Location.SequenceNumber != sequence) return null;
                if (element.ParentPath is { } parent)
                {
                    if (!order.TryGetValue(parent, out var children)) order.Add(parent, children = []);
                    children.Add(element.NodePath);
                }
            }
            var roots = new List<ParsedXmlElement>();
            foreach (var element in observations)
            {
                Tick();
                if (element.ParentPath is null) roots.Add(element);
                else if (!elements.ContainsKey(element.ParentPath)) return null;
                var actual = order.GetValueOrDefault(element.NodePath);
                if (element.Children.Count != (actual?.Count ?? 0)) return null;
                for (var index = 0; index < element.Children.Count; index++)
                {
                    Tick();
                    if (element.Children[index] != actual![index] || !elements.TryGetValue(element.Children[index], out var child) || child.ParentPath != element.NodePath) return null;
                }
            }
            if (roots.Count != 1) return null;
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
            while (pending.TryPop(out var current)) { Tick(); if (!visited.Add(current)) return null; foreach (var child in elements[current].Children) { Tick(); pending.Push(child); } }
            return visited.Count == elements.Count ? new(roots[0], elements) : null;
        }
        bool OwnerHeading(ParsedXmlElement leaf, ElementTree tree)
        {
            Tick();
            if (leaf.ParentPath is not { } path) return false;
            var current = tree.Elements[path];
            while (Plain(current, "node-extension")) { Tick(); if (current.ParentPath is not { } parent) return false; current = tree.Elements[parent]; }
            if (current.NamespaceUri.Length != 0 || !context.NodeSchema.Definitions.TryGetValue(current.Name, out var definition)) return false;
            while (definition.ParentDefinitionKey is { } parentKey)
            {
                Tick(); if (current.ParentPath is not { } parent || !Plain(tree.Elements[parent], parentKey)) return false;
                current = tree.Elements[parent]; definition = context.NodeSchema.Get(parentKey);
            }
            return current.ParentPath == tree.Root.NodePath;
        }
        bool CheckPassed(PackageXmlInspection xml, string id) { foreach (var check in xml.Checks) { Tick(); if (check.RuleId == id && check.Status == CheckStatus.Pass) return true; } return false; }
        bool Contains(IEnumerable<string> values, string value) { foreach (var candidate in values) { Tick(); if (candidate == value) return true; } return false; }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null; var count = 0;
            foreach (var attribute in element.Attributes.Values) { Tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault) { count++; value = attribute.Value; } }
            return count == 1 ? value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new OrderWorkLimitException(); }
    }
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed record ElementTree(ParsedXmlElement Root, IReadOnlyDictionary<string, ParsedXmlElement> Elements);
    private sealed class OrderWorkLimitException : Exception;
}
