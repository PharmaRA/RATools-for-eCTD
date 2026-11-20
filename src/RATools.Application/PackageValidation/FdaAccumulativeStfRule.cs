using System.Text.Json;
using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaAccumulativeStfRule
{
    internal const string RuleId = "FDA47-1731";

    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The accumulative STF inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1731" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1731 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1731.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) ||
            !context.Assets.Assets.TryGetValue("stf-dtd", out var asset) ||
            asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f")
            return Unknown("The archived STF specification and pinned STF DTD are required.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        var ownerIndex = new Dictionary<LeafAddress, List<(BoundStudyTaggingFile Document, StfOwnerBinding Owner)>>();
        var metadata = new Dictionary<(string, string), string?>();
        var emitted = new HashSet<(string, string)>();
        try
        {
            foreach (var document in bindings.Documents)
                foreach (var owner in document.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address is not { } address) { incomplete = true; continue; }
                    if (!ownerIndex.TryGetValue(address, out var entries)) ownerIndex.Add(address, entries = []);
                    entries.Add((document, owner));
                }
            foreach (var document in bindings.Documents)
            {
                Tick();
                var targetOwners = document.Owners.Where(owner => owner.Leaf.Address?.SequenceNumber == packages.Target.Manifest.SequenceNumber).ToArray();
                if (targetOwners.Length == 0) continue; // Historical defects are not target findings.
                if (!document.StructureComplete || targetOwners.Any(owner => !owner.Qualified)) { incomplete = true; continue; }
                var chains = new List<List<BoundStudyTaggingFile>>();
                var chainsKnown = true;
                foreach (var owner in targetOwners)
                {
                    Tick();
                    var ancestors = new List<BoundStudyTaggingFile>();
                    chainsKnown &= Chain(owner, ancestors);
                    chains.Add(ancestors);
                }
                if (!chainsKnown) incomplete = true;
                    foreach (var reference in document.References)
                    {
                        Tick();
                        if (reference.Status != StfReferenceStatus.Resolved || reference.Target is not { } address || reference.Leaf is null)
                        { incomplete = true; continue; }
                        // Identity is the XML leaf, not its physical PDF href or checksum.
                        if (address.SequenceNumber == packages.Target.Manifest.SequenceNumber && reference.Leaf.Operation is "new" or "append" or "replace") continue;
                        if (!chainsKnown) { incomplete = true; continue; }
                        var currentSignature = Signature(document, reference);
                        if (currentSignature is null) { incomplete = true; continue; }
                        var repeated = chains.Count > 0;
                        foreach (var ancestors in chains)
                        {
                            Tick();
                            var sameInChain = false;
                            // Nearest reference wins: an older equal tag cannot erase a later correction.
                            foreach (var prior in ancestors)
                            {
                                Tick();
                                var found = false;
                                var allEqual = true;
                                foreach (var old in prior.References)
                                {
                                    Tick();
                                    if (old.Target != address) continue;
                                    found = true;
                                    allEqual &= old.Status == StfReferenceStatus.Resolved && Signature(prior, old) == currentSignature;
                                }
                                if (!found) continue;
                                sameInChain = allEqual;
                                break;
                            }
                            repeated &= sameInChain;
                        }
                        // A changed tag/property or an unrelated historical reference is not itself proof of cumulative content.
                        if (!repeated) { incomplete = true; continue; }
                        if (ChangedAfterOrigin(address)) { incomplete = true; continue; }
                        if (!emitted.Add((document.Document.LogicalPath, reference.Element.NodePath))) continue;
                        if (remaining <= 0) { incomplete = true; continue; }
                        remaining--;
                        var attribute = reference.Element.Attributes.Values.Single(value => value.LocalName == "href" && value.NamespaceUri == "http://www.w3.org/1999/xlink");
                        findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                            "This STF repeats an unchanged historical document reference already supplied in its own STF lifecycle chain.",
                            reference.Element.Location with { FieldPath = "@xlink:href", Line = attribute.Line, Column = attribute.Column,
                                Files = [new(document.SourceId, document.Document.LogicalPath)] },
                            Expected: "Only documents added or modified in the current sequence", Actual: reference.Href));
                        retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A cumulative reference is established; remaining scope may be incomplete.", findings.ToArray()));
                    }
            }
        }
        catch (StfWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Compared exact target STF owners with their selected predecessor chains. Current leaf additions, reused physical files and identifier-only corrections are distinct from repeated unchanged historical references. Unavailable history or ambiguous corrections remain unevaluated.", findings);

        void Tick()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) throw new StfWorkLimitException();
        }

        bool Chain(StfOwnerBinding start, List<BoundStudyTaggingFile> ancestors)
        {
            var owner = start;
            var visited = new HashSet<LeafAddress>();
            while (true)
            {
                Tick();
                if (!owner.Qualified || owner.Leaf.Address is not { } address || !visited.Add(address) || !owner.Leaf.ContextComplete) return false;
                if (owner.Leaf.Operation == "new") return string.IsNullOrEmpty(owner.Leaf.ModifiedFile);
                if (owner.Leaf.Operation != "append" || string.IsNullOrEmpty(owner.Leaf.ModifiedFile)) return false;
                var previous = Resolve(owner.BackboneLogicalPath, owner.Leaf.ModifiedFile);
                if (previous is null || string.CompareOrdinal(previous.SequenceNumber, address.SequenceNumber) >= 0 ||
                    !ownerIndex.TryGetValue(previous, out var candidates) || candidates.Count != 1) return false;
                var prior = candidates[0];
                if (!prior.Owner.Qualified || !prior.Document.StructureComplete || !prior.Owner.Leaf.ContextComplete ||
                    prior.Owner.Heading != owner.Heading || prior.Owner.Leaf.ContextKey != owner.Leaf.ContextKey) return false;
                ancestors.Add(prior.Document);
                owner = prior.Owner;
            }
        }

        LeafAddress? Resolve(string backbone, string href)
        {
            try
            {
                var resolved = PackageLogicalPath.ResolveReference(backbone, href);
                if (resolved.LogicalPath is not { } path || resolved.Query is not null || string.IsNullOrEmpty(resolved.Fragment)) return null;
                var parts = path.Split('/', 2);
                return parts.Length == 2 ? new(packages.Baseline.ApplicationId, parts[0], parts[1], resolved.Fragment) : null;
            }
            catch (Exception exception) when (exception is ArgumentException or System.Xml.XmlException) { return null; }
        }

        bool ChangedAfterOrigin(LeafAddress address)
        {
            foreach (var xml in lifecycle.XmlInspections)
                foreach (var backbone in xml.Backbones)
                    foreach (var leaf in backbone.Leaves)
                    {
                        Tick();
                        if (leaf.Address is not { } eventAddress || string.CompareOrdinal(eventAddress.SequenceNumber, address.SequenceNumber) < 0 ||
                            leaf.Operation is not ("append" or "replace" or "delete") || string.IsNullOrEmpty(leaf.ModifiedFile)) continue;
                        if (Resolve(backbone.LogicalPath, leaf.ModifiedFile) == address) return true;
                    }
            return false;
        }

        string? Signature(BoundStudyTaggingFile document, StfDocumentReference reference)
        {
            var key = (document.Document.LogicalPath, reference.Element.NodePath);
            if (metadata.TryGetValue(key, out var cached)) return cached;
            var nodes = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in document.Document.Elements) { Tick(); if (!nodes.TryAdd(element.NodePath, element)) return metadata[key] = null; }
            var parts = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = reference.Element;
            while (true)
            {
                Tick();
                if (!visited.Add(current.NodePath)) return metadata[key] = null;
                if (current.LocalName == "study-document" && current.NamespaceUri.Length == 0) break;
                if (current.NamespaceUri.Length != 0 || current.LocalName is not ("doc-content" or "content-block")) return metadata[key] = null;
                parts.Add(current.LocalName);
                var childParts = new List<string>();
                var propertyKeys = new HashSet<(string?, string?)>();
                foreach (var childPath in current.Children)
                {
                    Tick();
                    if (!nodes.TryGetValue(childPath, out var child)) return metadata[key] = null;
                    if (child.LocalName is "doc-content" or "content-block") continue;
                    if (child.LocalName == "property" && !propertyKeys.Add((child.Attribute("name", ""), child.Attribute("info-type", "")))) return metadata[key] = null;
                    var part = Serialize(child, new HashSet<string>(StringComparer.Ordinal));
                    if (part is null) return metadata[key] = null;
                    childParts.Add(part);
                }
                parts.AddRange(childParts.Order(StringComparer.Ordinal));
                // The href identifies the leaf, not metadata. Other explicit attributes (e.g. test) are retained.
                foreach (var value in current.Attributes.Values.OrderBy(value => value.NamespaceUri, StringComparer.Ordinal).ThenBy(value => value.LocalName, StringComparer.Ordinal))
                {
                    Tick();
                    if (value.NamespaceUri == "http://www.w3.org/1999/xlink" || value.NamespaceUri == "http://www.w3.org/2000/xmlns/") continue;
                    parts.Add(JsonSerializer.Serialize(new[] { value.NamespaceUri, value.LocalName, value.Value }));
                }
                if (current.ParentPath is not { } parent || !nodes.TryGetValue(parent, out current!)) return metadata[key] = null;
            }
            return metadata[key] = JsonSerializer.Serialize(parts);

            string? Serialize(ParsedXmlElement element, HashSet<string> seen)
            {
                Tick();
                if (!seen.Add(element.NodePath) || element.NamespaceUri.Length != 0 || element.LocalName is not ("title" or "block-title" or "property" or "file-tag")) return null;
                var values = new List<string> { element.LocalName, element.Text };
                foreach (var value in element.Attributes.Values.OrderBy(value => value.NamespaceUri, StringComparer.Ordinal).ThenBy(value => value.LocalName, StringComparer.Ordinal))
                { Tick(); values.Add(JsonSerializer.Serialize(new[] { value.NamespaceUri, value.LocalName, value.Value })); }
                var childValues = new List<string>();
                var propertyKeys = new HashSet<(string?, string?)>();
                foreach (var path in element.Children)
                {
                    Tick();
                    if (!nodes.TryGetValue(path, out var child)) return null;
                    if (child.LocalName == "property" && !propertyKeys.Add((child.Attribute("name", ""), child.Attribute("info-type", "")))) return null;
                    var text = Serialize(child, seen);
                    if (text is null) return null;
                    childValues.Add(text);
                }
                values.AddRange(childValues.Order(StringComparer.Ordinal));
                return JsonSerializer.Serialize(values);
            }
        }
    }

    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class StfWorkLimitException : Exception;
}
