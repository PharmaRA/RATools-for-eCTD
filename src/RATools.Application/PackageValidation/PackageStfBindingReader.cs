using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageStfBindingReader
{
    internal static PackageStfBindings Read(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (lifecycle.InputDigest != packages.Target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.LimitsDigest != packages.Target.Limits.Digest() || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest) return PackageStfBindings.Unknown;
        var remaining = packages.Target.Limits.MaxXmlNodes;
        var complete = true;
        var result = new List<BoundStudyTaggingFile>();
        var sequences = new Dictionary<string, SequenceFacts>(StringComparer.Ordinal);
        try
        {
            foreach (var (number, capture) in packages.Sequences.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Tick();
                var selectedContext = context.ForSequence(number);
                var profile = number == packages.Target.Manifest.SequenceNumber ? lifecycle.ProfileSnapshotId :
                    packages.Baseline.Entries.Single(entry => entry.SequenceNumber == number).ProfileSnapshotId;
                var matches = new List<PackageXmlInspection>();
                foreach (var xml in lifecycle.XmlInspections)
                {
                    Tick();
                    if (xml.InputDigest == capture.InputDigest && xml.ProfileSnapshotId == profile) matches.Add(xml);
                }
                if (matches.Count != 1 || matches[0].RulesDigest != selectedContext.Catalog.Digest || matches[0].AssetManifestDigest != selectedContext.Assets.Digest ||
                    matches[0].LimitsDigest != capture.Limits.Digest()) { complete = false; continue; }
                var observation = matches[0];
                var files = new Dictionary<string, PackageInputFile>(StringComparer.Ordinal);
                foreach (var file in capture.Manifest.Files) { Tick(); if (!files.TryAdd(file.LogicalPath, file)) complete = false; }
                var backbones = new Dictionary<string, List<BackboneFacts>>(StringComparer.Ordinal);
                foreach (var document in observation.Backbones)
                {
                    Tick();
                    var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                    var children = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                    var leaves = new Dictionary<string, List<ParsedPackageLeaf>>(StringComparer.Ordinal);
                    var unique = true;
                    foreach (var element in document.Elements)
                    {
                        Tick();
                        unique &= elements.TryAdd(element.NodePath, element);
                        var members = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var child in element.Children) { Tick(); unique &= members.Add(child); }
                        children.TryAdd(element.NodePath, members);
                        foreach (var attribute in element.Attributes.Values) { Tick(); _ = attribute; }
                    }
                    foreach (var leaf in document.Leaves)
                    {
                        Tick();
                        if (!leaves.TryGetValue(leaf.NodePath, out var entries)) leaves.Add(leaf.NodePath, entries = []);
                        entries.Add(leaf);
                    }
                    foreach (var ids in document.IdIndex.Values) foreach (var id in ids) { Tick(); _ = id; }
                    if (!backbones.TryGetValue(document.LogicalPath, out var documents)) backbones.Add(document.LogicalPath, documents = []);
                    documents.Add(new(document, elements, children, leaves, unique));
                }
                sequences.Add(number, new(capture, observation, selectedContext, files, backbones));
            }
            // Owners are collected across all selected captures: a new leaf may refer to an older STF file.
            var owners = new Dictionary<string, List<StfOwnerBinding>>(StringComparer.Ordinal);
            foreach (var (number, facts) in sequences)
            {
                Tick();
                foreach (var documents in facts.Backbones.Values)
                    foreach (var index in documents)
                    {
                        Tick();
                        if (index.Document.Kind != BackboneKind.Ich || index.Document.LogicalPath != number + "/index.xml") continue;
                        foreach (var entries in index.Leaves.Values)
                            foreach (var leaf in entries)
                            {
                                Tick();
                                if (leaf.Operation == "delete" || leaf.IsRegionalReference || leaf.Href is null) continue;
                                PackageReference reference;
                                try { reference = PackageLogicalPath.ResolveReference(index.Document.LogicalPath, leaf.Href); }
                                catch (ArgumentException) { complete = false; continue; }
                                if (reference.LogicalPath is not { } path) continue;
                                if (!owners.TryGetValue(path, out var candidates)) owners.Add(path, candidates = []);
                                var heading = Heading(leaf, index.Elements, index.Children, facts.Context);
                                var qualified = documents.Count == 1 && index.UniqueElements && entries.Count == 1 &&
                                    index.Document.ReadComplete && index.Document.DtdStatus == CheckStatus.Pass &&
                                    reference.Query is null && reference.Fragment is null && leaf.Address is not null &&
                                    index.Elements.TryGetValue(leaf.NodePath, out var element) &&
                                    Plain(element, "leaf") && Explicit(element, "ID", "") == leaf.Id &&
                                    Explicit(element, "operation", "") is "new" or "append" or "replace" &&
                                    Explicit(element, "operation", "") == leaf.Operation &&
                                    Explicit(element, "href", "http://www.w3c.org/1999/xlink") == leaf.Href && heading is not null;
                                candidates.Add(new(index.Document.LogicalPath, leaf, heading, qualified));
                            }
                    }
            }
            foreach (var (number, facts) in sequences)
            {
                Tick();
                var scopeComplete = facts.Xml.StudyTaggingFiles.InventoryComplete;
                var stfPaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var document in facts.Xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    if (!stfPaths.Add(document.LogicalPath)) { scopeComplete = false; continue; }
                    if (!document.LogicalPath.StartsWith(number + "/", StringComparison.Ordinal) ||
                        !facts.Files.TryGetValue(document.LogicalPath, out var file) || file.Sha256 != document.CapturedSha256)
                    { scopeComplete = false; continue; }
                    var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                    var children = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                    var localComplete = document.ReadComplete && document.DtdStatus == CheckStatus.Pass;
                    foreach (var element in document.Elements)
                    {
                        Tick();
                        localComplete &= elements.TryAdd(element.NodePath, element);
                        var members = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var child in element.Children) { Tick(); localComplete &= members.Add(child); }
                        children.TryAdd(element.NodePath, members);
                        foreach (var attribute in element.Attributes.Values) { Tick(); _ = attribute; }
                    }
                    var candidates = owners.GetValueOrDefault(document.LogicalPath) ?? [];
                    localComplete &= candidates.Count > 0 && candidates.All(owner => owner.Qualified);
                    var references = new List<StfDocumentReference>();
                    foreach (var element in document.Elements)
                    {
                        Tick();
                        if (!Plain(element, "doc-content")) continue;
                        if (!InStudyDocument(element, elements, children)) { localComplete = false; continue; }
                        var href = Explicit(element, "href", "http://www.w3.org/1999/xlink");
                        var reference = Resolve(number, document.LogicalPath, element, href);
                        references.Add(reference);
                        localComplete &= reference.Status == StfReferenceStatus.Resolved;
                    }
                    Tick(); // One shared budget also bounds expanded owner/reference associations.
                    foreach (var owner in candidates) { Tick(); _ = owner; }
                    foreach (var reference in references) { Tick(); _ = reference; }
                    result.Add(new(facts.Capture.SourceId, facts.Capture.InputDigest, number == packages.Target.Manifest.SequenceNumber,
                        document, localComplete, candidates, references));
                    scopeComplete &= localComplete;
                }
                complete &= scopeComplete;
            }
        }
        catch (BindingLimitException) { complete = false; }
        token.ThrowIfCancellationRequested();
        return new(complete, result);

        void Tick()
        {
            token.ThrowIfCancellationRequested();
            if (remaining-- <= 0) throw new BindingLimitException();
        }

        string? Heading(ParsedPackageLeaf leaf, IReadOnlyDictionary<string, ParsedXmlElement> elements,
            IReadOnlyDictionary<string, HashSet<string>> children, PackageStandardsContext selected)
        {
            if (!elements.TryGetValue(leaf.NodePath, out var current)) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ParsedXmlElement? Parent()
            {
                Tick();
                if (!seen.Add(current.NodePath) || current.ParentPath is not { } path || !elements.TryGetValue(path, out var parent) ||
                    !children[parent.NodePath].Contains(current.NodePath)) return null;
                current = parent;
                return parent;
            }
            var heading = Parent();
            while (heading is not null && Plain(heading, "node-extension")) heading = Parent();
            if (heading is null || heading.NamespaceUri.Length != 0 || !selected.NodeSchema.Definitions.TryGetValue(heading.Name, out var definition)) return null;
            var name = heading.Name;
            while (definition.ParentDefinitionKey is { } parentKey)
            {
                var parent = Parent();
                if (parent is null || !Plain(parent, parentKey)) return null;
                definition = selected.NodeSchema.Get(parentKey);
            }
            return Parent() is { ParentPath: null, LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } ? name : null;
        }

        bool InStudyDocument(ParsedXmlElement element, IReadOnlyDictionary<string, ParsedXmlElement> elements,
            IReadOnlyDictionary<string, HashSet<string>> children)
        {
            var current = element;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (current.ParentPath is { } path)
            {
                Tick();
                if (!seen.Add(current.NodePath) || !elements.TryGetValue(path, out var parent) || !children[parent.NodePath].Contains(current.NodePath)) return false;
                if (Plain(parent, "content-block")) { current = parent; continue; }
                return Plain(parent, "study-document") && parent.ParentPath is { } rootPath && elements.TryGetValue(rootPath, out var root) &&
                    root is { ParentPath: null, LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" } && children[root.NodePath].Contains(parent.NodePath);
            }
            return false;
        }

        StfDocumentReference Resolve(string sequence, string stfPath, ParsedXmlElement element, string? href)
        {
            Tick();
            PackageReference reference;
            try { reference = PackageLogicalPath.ResolveReference(stfPath, href!); }
            catch (ArgumentException) { return Unresolved(StfReferenceStatus.InvalidReference); }
            if (reference.LogicalPath is not { } path || reference.Query is not null || string.IsNullOrEmpty(reference.Fragment)) return Unresolved(StfReferenceStatus.InvalidReference);
            LeafAddress address;
            try
            {
                var slash = path.IndexOf('/');
                if (slash < 0) return Unresolved(StfReferenceStatus.InvalidReference);
                address = new(packages.Target.Manifest.ApplicationId, path[..slash], path[(slash + 1)..], reference.Fragment);
            }
            catch (Exception error) when (error is ArgumentException or System.Xml.XmlException) { return Unresolved(StfReferenceStatus.InvalidReference); }
            if (string.CompareOrdinal(address.SequenceNumber, sequence) > 0) return Unresolved(StfReferenceStatus.FutureReference, address);
            if (!packages.Sequences.ContainsKey(address.SequenceNumber)) return Unresolved(StfReferenceStatus.HistoryUnavailable, address);
            if (!sequences.TryGetValue(address.SequenceNumber, out var facts) || !facts.Files.ContainsKey(path) ||
                !facts.Backbones.TryGetValue(path, out var documents)) return Unresolved(StfReferenceStatus.BackboneUnavailable, address);
            if (documents.Count != 1) return Unresolved(StfReferenceStatus.BackboneIncomplete, address);
            var backbone = documents[0];
            if (!backbone.Document.ReadComplete) return Unresolved(StfReferenceStatus.BackboneIncomplete, address);
            if (!backbone.UniqueElements || backbone.Document.DtdStatus != CheckStatus.Pass) return Unresolved(StfReferenceStatus.BackboneInvalid, address);
            if (!backbone.Document.IdIndex.TryGetValue(address.LeafId, out var ids)) return Unresolved(StfReferenceStatus.LeafMissing, address);
            if (ids.Count != 1) return Unresolved(StfReferenceStatus.AmbiguousId, address);
            if (!ids[0].IsLeaf) return Unresolved(StfReferenceStatus.NotLeaf, address);
            if (!backbone.Leaves.TryGetValue(ids[0].NodePath, out var leaves) || leaves.Count != 1 || leaves[0].Address != address ||
                leaves[0].Operation is not ("new" or "append" or "replace")) return Unresolved(StfReferenceStatus.LeafUnqualified, address);
            if (!backbone.Elements.TryGetValue(ids[0].NodePath, out var leafElement) || !Plain(leafElement, "leaf") ||
                Explicit(leafElement, "ID", "") != address.LeafId || Explicit(leafElement, "operation", "") != leaves[0].Operation)
                return Unresolved(StfReferenceStatus.LeafUnqualified, address);
            return new(element, href, StfReferenceStatus.Resolved, address, leaves[0]);
            StfDocumentReference Unresolved(StfReferenceStatus status, LeafAddress? target = null) => new(element, href, status, target, null);
        }
    }

    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static string? Explicit(ParsedXmlElement element, string name, string ns) => element.Attributes.Values
        .SingleOrDefault(attribute => attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault)?.Value;
    private sealed class BindingLimitException : Exception;
    private sealed record BackboneFacts(ParsedBackbone Document, IReadOnlyDictionary<string, ParsedXmlElement> Elements,
        IReadOnlyDictionary<string, HashSet<string>> Children, IReadOnlyDictionary<string, List<ParsedPackageLeaf>> Leaves, bool UniqueElements);
    private sealed record SequenceFacts(ICapturedPackageInput Capture, PackageXmlInspection Xml, PackageStandardsContext Context,
        IReadOnlyDictionary<string, PackageInputFile> Files, IReadOnlyDictionary<string, List<BackboneFacts>> Backbones);
}
