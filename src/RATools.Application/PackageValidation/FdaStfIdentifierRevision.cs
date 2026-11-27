using System.Text.Json;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// A bounded positive exception for a direct identifier-only append; not cumulative STF reconstruction.
internal static class FdaStfIdentifierRevision
{
    internal static bool Qualified(BoundStudyTaggingFile current, IReadOnlyList<StfOwnerBinding> owners,
        PackageStfBindings bindings, PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, Action tick)
    {
        tick();
        if (!current.IsTarget || owners.Count != 1 || current.References.Count != 0 || !current.StructureComplete) return false;
        var owner = owners[0];
        if (owner.Leaf.Operation != "append" || owner.Leaf.Address is not { } address ||
            !OwnerBound(current, owner, "append", out var modified) || string.IsNullOrEmpty(modified)) return false;
        LeafAddress previousAddress;
        try
        {
            var reference = PackageLogicalPath.ResolveReference(owner.BackboneLogicalPath, modified);
            if (reference.LogicalPath is not { } path || reference.Query is not null || string.IsNullOrEmpty(reference.Fragment)) return false;
            var parts = path.Split('/', 2);
            if (parts.Length != 2) return false;
            previousAddress = new(packages.Target.Manifest.ApplicationId, parts[0], parts[1], reference.Fragment);
        }
        catch (Exception error) when (error is ArgumentException or System.Xml.XmlException) { return false; }
        if (string.CompareOrdinal(previousAddress.SequenceNumber, address.SequenceNumber) >= 0) return false;
        var previous = new List<(BoundStudyTaggingFile Document, StfOwnerBinding Owner)>();
        foreach (var document in bindings.Documents)
            foreach (var candidate in document.Owners)
            { tick(); if (candidate.Leaf.Address == previousAddress) previous.Add((document, candidate)); }
        if (previous.Count != 1) return false;
        var prior = previous[0];
        if (prior.Document.IsTarget || prior.Document.Owners.Count != 1 || !prior.Document.StructureComplete ||
            !OwnerBound(prior.Document, prior.Owner, "new", out var priorModified) || priorModified is not null ||
            prior.Owner.Heading != owner.Heading || prior.Owner.Leaf.ContextKey != owner.Leaf.ContextKey) return false;
        var operations = new List<PackageLifecycleEvent>();
        foreach (var operation in lifecycle.Events)
        {
            tick();
            if (operation.Source.Address == address) operations.Add(operation);
            else if (operation.Target == previousAddress) return false; // A competing or later selected update is not this direct predecessor.
        }
        if (operations.Count != 1 || operations[0].Source != owner.Leaf || operations[0].Status != CheckStatus.Pass ||
            operations[0].TargetLeafLookup != CheckStatus.Pass || operations[0].Target != previousAddress ||
            operations[0].TargetContentLogicalPath != prior.Document.Document.LogicalPath ||
            !Effective(address) || !Effective(previousAddress)) return false;
        var hasAssociation = false;
        foreach (var reference in prior.Document.References)
        {
            tick();
            if (reference.Status == StfReferenceStatus.Resolved && reference.Target is { } leaf && reference.Leaf?.Address == leaf && Effective(leaf))
                hasAssociation = true;
        }
        if (!hasAssociation) return false;
        var now = Identifier(current, true);
        var before = Identifier(prior.Document, false);
        // Study-id changes and deletion of categories need their own policy; do not infer them from a changed title.
        return now is not null && before is not null && now.Value.StudyId == before.Value.StudyId && now.Value.Signature != before.Value.Signature &&
            before.Value.CategoryKeys.All(key => now.Value.CategoryKeys.Contains(key));

        bool Effective(LeafAddress leaf)
        {
            var matches = 0; var currentState = false;
            foreach (var state in lifecycle.States)
            { tick(); if (state.Address == leaf) { matches++; currentState = state.Effectiveness == PackageLeafEffectiveness.Current; } }
            return matches == 1 && currentState;
        }

        bool OwnerBound(BoundStudyTaggingFile document, StfOwnerBinding candidate, string operation, out string? modifiedFile)
        {
            modifiedFile = null;
            if (!candidate.Qualified || !candidate.Leaf.ContextComplete || candidate.Leaf.Address is not { } leaf ||
                leaf.ApplicationId != packages.Target.Manifest.ApplicationId || leaf.LeafId != candidate.Leaf.Id ||
                candidate.BackboneLogicalPath != leaf.SequenceNumber + "/" + leaf.BackboneRelativePath ||
                !packages.Sequences.TryGetValue(leaf.SequenceNumber, out var capture) || document.SourceId != capture.SourceId ||
                document.InputDigest != capture.InputDigest || !document.Document.LogicalPath.StartsWith(leaf.SequenceNumber + "/", StringComparison.Ordinal)) return false;
            var selected = context.ForSequence(leaf.SequenceNumber);
            var profile = lifecycle.ProfileSnapshotId;
            if (leaf.SequenceNumber != packages.Target.Manifest.SequenceNumber)
            {
                var entries = packages.Baseline.Entries.Where(entry => entry.SequenceNumber == leaf.SequenceNumber).Take(2).ToArray();
                if (entries.Length != 1) return false;
                profile = entries[0].ProfileSnapshotId;
            }
            var xmls = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections)
            { tick(); if (xml.InputDigest == capture.InputDigest && xml.ProfileSnapshotId == profile) xmls.Add(xml); }
            if (xmls.Count != 1 || xmls[0].RulesDigest != selected.Catalog.Digest || xmls[0].AssetManifestDigest != selected.Assets.Digest ||
                xmls[0].LimitsDigest != capture.Limits.Digest()) return false;
            var stfObservations = 0;
            foreach (var observed in xmls[0].StudyTaggingFiles.Documents)
            {
                tick();
                if (observed.LogicalPath != document.Document.LogicalPath) continue;
                stfObservations++;
                if (observed.CapturedSha256 != document.Document.CapturedSha256) return false;
            }
            if (stfObservations != 1) return false;
            var backbones = new List<ParsedBackbone>();
            foreach (var backbone in xmls[0].Backbones)
            { tick(); if (backbone.LogicalPath == candidate.BackboneLogicalPath) backbones.Add(backbone); }
            if (backbones.Count != 1 || backbones[0].ProfileSnapshotId != profile || backbones[0].Kind != BackboneKind.Ich ||
                !backbones[0].ReadComplete || backbones[0].DtdStatus != CheckStatus.Pass) return false;
            var elements = new List<ParsedXmlElement>();
            foreach (var element in backbones[0].Elements)
            { tick(); if (element.NodePath == candidate.Leaf.NodePath) elements.Add(element); }
            if (elements.Count != 1 || elements[0] is not { LocalName: "leaf", NamespaceUri.Length: 0 }) return false;
            var original = elements[0];
            if (original.Location.LogicalPath != candidate.BackboneLogicalPath || original.Location.NodePath != candidate.Leaf.NodePath ||
                candidate.Leaf.Location.LogicalPath != candidate.BackboneLogicalPath || candidate.Leaf.Location.NodePath != candidate.Leaf.NodePath ||
                Explicit(original, "ID", "") != leaf.LeafId || Explicit(original, "operation", "") != operation || candidate.Leaf.Operation != operation ||
                Explicit(original, "href", "http://www.w3c.org/1999/xlink") is not { } href || href != candidate.Leaf.Href) return false;
            var modifiedAttributes = 0;
            foreach (var attribute in original.Attributes.Values)
            { tick(); if (attribute.LocalName == "modified-file" && attribute.NamespaceUri.Length == 0) modifiedAttributes++; }
            if (operation == "new")
            { if (modifiedAttributes != 0 || !string.IsNullOrEmpty(candidate.Leaf.ModifiedFile)) return false; }
            else
            {
                modifiedFile = Explicit(original, "modified-file", "");
                if (modifiedAttributes != 1 || string.IsNullOrEmpty(modifiedFile) || modifiedFile != candidate.Leaf.ModifiedFile) return false;
            }
            try
            {
                var content = PackageLogicalPath.ResolveReference(candidate.BackboneLogicalPath, href);
                return content.Query is null && content.Fragment is null && content.LogicalPath == document.Document.LogicalPath;
            }
            catch (ArgumentException) { return false; }
        }

        (string StudyId, string Signature, HashSet<(string, string)> CategoryKeys)? Identifier(BoundStudyTaggingFile bound, bool emptyDocument)
        {
            var document = bound.Document;
            if (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass) return null;
            var nodes = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in document.Elements)
            {
                tick();
                if (!nodes.TryAdd(element.NodePath, element) || element.Location.LogicalPath != document.LogicalPath || element.Location.NodePath != element.NodePath) return null;
            }
            var roots = nodes.Values.Where(element => element.ParentPath is null).Take(2).ToArray();
            if (roots.Length != 1 || roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" }) return null;
            var ownership = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parent in nodes.Values)
                foreach (var childPath in parent.Children)
                {
                    tick();
                    if (!nodes.TryGetValue(childPath, out var child) || child.ParentPath != parent.NodePath || !ownership.Add(childPath)) return null;
                }
            if (ownership.Count != nodes.Count - 1) return null;
            var children = roots[0].Children.Select(path => nodes[path]).ToArray();
            if (children.Length != 2 || children[0] is not { LocalName: "study-identifier", NamespaceUri.Length: 0 } ||
                children[1] is not { LocalName: "study-document", NamespaceUri.Length: 0 } ||
                emptyDocument && (children[1].Children.Count != 0 || !string.IsNullOrWhiteSpace(children[1].Text))) return null;
            var identifier = children[0];
            var parts = identifier.Children.Select(path => nodes[path]).ToArray();
            if (parts.Length < 2 || parts[0].LocalName != "title" || parts[1].LocalName != "study-id" ||
                string.IsNullOrWhiteSpace(parts[0].Text) || string.IsNullOrWhiteSpace(parts[1].Text)) return null;
            var categories = new List<string>();
            var keys = new HashSet<(string, string)>();
            for (var index = 0; index < parts.Length; index++)
            {
                tick(); var part = parts[index];
                if (part.NamespaceUri.Length != 0 || part.Children.Count != 0) return null;
                if (index < 2) { if (part.Attributes.Count != 0) return null; continue; }
                if (part.LocalName != "category" || part.Attributes.Count != 2 || Explicit(part, "name", "") is not { Length: > 0 } name ||
                    Explicit(part, "info-type", "") is not { Length: > 0 } info || string.IsNullOrWhiteSpace(part.Text)) return null;
                keys.Add((name, info));
                categories.Add(JsonSerializer.Serialize(new[] { name, info, part.Text }));
            }
            return (parts[1].Text, JsonSerializer.Serialize(new { title = parts[0].Text, categories = categories.Order(StringComparer.Ordinal).ToArray() }), keys);
        }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            var count = 0; string? value = null;
            foreach (var attribute in element.Attributes.Values)
            { tick(); if (attribute.LocalName == name && attribute.NamespaceUri == ns) { count++; if (!attribute.IsDefault && attribute.Value == attribute.ExplicitValue) value = attribute.ExplicitValue; } }
            return count == 1 ? value : null;
        }
    }
}
