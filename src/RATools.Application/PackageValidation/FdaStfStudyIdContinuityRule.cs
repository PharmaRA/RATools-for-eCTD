using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfStudyIdContinuityRule
{
    internal const string RuleId = "FDA47-1850";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF study identity continuity inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1850" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1850 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1850.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured STF specification is required.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var owners = new Dictionary<LeafAddress, List<(BoundStudyTaggingFile Document, StfOwnerBinding Owner)>>();
            var observations = new Dictionary<(string Digest, string Path), int>();
            var xmlByDigest = new Dictionary<string, List<PackageXmlInspection>>(StringComparer.Ordinal);
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                if (!xmlByDigest.TryGetValue(xml.InputDigest, out var matches)) xmlByDigest.Add(xml.InputDigest, matches = []);
                matches.Add(xml);
                foreach (var document in xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    var key = (xml.InputDigest, document.LogicalPath);
                    observations[key] = observations.GetValueOrDefault(key) + 1;
                }
                if (xml.InputDigest == packages.Target.InputDigest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId &&
                    xml.RulesDigest == context.Catalog.Digest && xml.AssetManifestDigest == context.Assets.Digest &&
                    xml.LimitsDigest == packages.Target.Limits.Digest())
                {
                    foreach (var index in xml.Backbones)
                    {
                        Tick();
                        if (index.Kind != BackboneKind.Ich || index.LogicalPath != packages.Target.Manifest.SequenceNumber + "/index.xml") continue;
                        // The shared binding reader excludes derived delete leaves. Check raw
                        // append candidates here before that exclusion can imply no applicability.
                        var leafFacts = new Dictionary<string, List<ParsedPackageLeaf>>(StringComparer.Ordinal);
                        foreach (var leaf in index.Leaves)
                        {
                            Tick();
                            if (!leafFacts.TryGetValue(leaf.NodePath, out var facts)) leafFacts.Add(leaf.NodePath, facts = []);
                            facts.Add(leaf);
                        }
                        foreach (var element in index.Elements)
                        {
                            Tick();
                            if (element is not { Name: "leaf", LocalName: "leaf", NamespaceUri.Length: 0 }) continue;
                            var rawOperation = Explicit(element, "operation");
                            if (rawOperation == "append" && (!leafFacts.TryGetValue(element.NodePath, out var facts) ||
                                facts.Count != 1 || facts[0].Operation != rawOperation) && !KnownNonStfTarget(xml, index, element)) incomplete = true;
                        }
                    }
                }
            }
            foreach (var document in bindings.Documents)
                foreach (var owner in document.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address is not { } address) continue;
                    if (!owners.TryGetValue(address, out var entries)) owners.Add(address, entries = []);
                    entries.Add((document, owner));
                }
            foreach (var (address, entries) in owners)
            {
                Tick();
                if (address.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                if (entries.Count != 1) { incomplete = true; continue; }
                var current = entries[0];
                var element = OwnerElement(current.Owner);
                if (!current.Owner.Qualified || element is null) { incomplete = true; continue; }
                var operation = Explicit(element, "operation");
                if (operation is null || operation != current.Owner.Leaf.Operation) { incomplete = true; continue; }
                if (operation is "new" or "replace" or "delete") continue;
                if (operation != "append") { incomplete = true; continue; }
                applicable = true;
                var modified = Explicit(element, "modified-file");
                if (string.IsNullOrWhiteSpace(modified) || modified != current.Owner.Leaf.ModifiedFile)
                { incomplete = true; continue; }
                LeafAddress previousAddress;
                try
                {
                    var reference = PackageLogicalPath.ResolveReference(current.Owner.BackboneLogicalPath, modified);
                    if (reference.LogicalPath is not { } path || reference.Query is not null || string.IsNullOrEmpty(reference.Fragment))
                    { incomplete = true; continue; }
                    var parts = path.Split('/', 2);
                    if (parts.Length != 2) { incomplete = true; continue; }
                    previousAddress = new(packages.Target.Manifest.ApplicationId, parts[0], parts[1], reference.Fragment);
                }
                catch (Exception error) when (error is ArgumentException or System.Xml.XmlException) { incomplete = true; continue; }
                if (string.CompareOrdinal(previousAddress.SequenceNumber, address.SequenceNumber) >= 0 ||
                    !owners.TryGetValue(previousAddress, out var predecessors) || predecessors.Count != 1 || !predecessors[0].Owner.Qualified)
                { incomplete = true; continue; }
                var previous = predecessors[0];
                // Compare only the explicitly linked direct edge. Earlier append defects,
                // unavailable ancestors and a replace predecessor do not redefine this pair.
                var currentId = StudyId(current.Document);
                var previousId = StudyId(previous.Document);
                if (currentId is null || previousId is null) { incomplete = true; continue; }
                if (string.Equals(currentId, previousId, StringComparison.Ordinal)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The appended STF study-id is not an exact match for its directly referenced predecessor STF.",
                    current.Owner.Leaf.Location with { FieldPath = "@modified-file/study-identifier/study-id", Files =
                        [new(packages.Target.SourceId, current.Owner.BackboneLogicalPath), new(current.Document.SourceId, current.Document.Document.LogicalPath),
                         new(packages.Sequences[previousAddress.SequenceNumber].SourceId, previous.Owner.BackboneLogicalPath),
                         new(previous.Document.SourceId, previous.Document.Document.LogicalPath)] },
                    Expected: previousId, Actual: currentId,
                    RecommendedAction: "Review the exact predecessor " + previous.Owner.BackboneLogicalPath + "#" + previousAddress.LeafId + " and the appended study identity."));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A direct STF append edge has a study identity mismatch; other edges may remain unqualified.", findings.ToArray()));
            }

            bool KnownNonStfTarget(PackageXmlInspection xml, ParsedBackbone index, ParsedXmlElement element)
            {
                Tick();
                if (!xml.StudyTaggingFiles.InventoryComplete) return false;
                string? href = null;
                foreach (var attribute in element.Attributes.Values)
                {
                    Tick();
                    if (attribute is { LocalName: "href", NamespaceUri: "http://www.w3c.org/1999/xlink", IsDefault: false }) href = attribute.Value;
                }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, href!); }
                catch (ArgumentException) { return false; }
                var prefix = packages.Target.Manifest.SequenceNumber + "/";
                if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null ||
                    !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(prefix + "util/", StringComparison.Ordinal)) return false;
                var files = 0;
                foreach (var file in packages.Target.Manifest.Files) { Tick(); if (file.LogicalPath == path) files++; }
                if (files != 1) return false;
                foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == path) return false; }
                foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) return false; }
                return true;
            }

            ParsedXmlElement? OwnerElement(StfOwnerBinding owner)
            {
                Tick();
                if (owner.Leaf.Address is not { } address || !packages.Sequences.TryGetValue(address.SequenceNumber, out var capture) ||
                    !xmlByDigest.TryGetValue(capture.InputDigest, out var matches) || matches.Count != 1) return null;
                var documents = new List<ParsedBackbone>();
                foreach (var document in matches[0].Backbones) { Tick(); if (document.LogicalPath == owner.BackboneLogicalPath) documents.Add(document); }
                if (documents.Count != 1) return null;
                var elements = new List<ParsedXmlElement>();
                foreach (var element in documents[0].Elements) { Tick(); if (element.NodePath == owner.Leaf.NodePath) elements.Add(element); }
                return elements.Count == 1 && elements[0] is { Name: "leaf", LocalName: "leaf", NamespaceUri.Length: 0 } ? elements[0] : null;
            }

            string? StudyId(BoundStudyTaggingFile bound)
            {
                Tick();
                var document = bound.Document;
                // Text has no independent closing-tag fact. Do not compare a retained prefix
                // from a truncated document, even when the opening element is already present.
                if (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass ||
                    observations.GetValueOrDefault((bound.InputDigest, document.LogicalPath)) != 1) return null;
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                var reverseChildren = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                var roots = new List<ParsedXmlElement>();
                foreach (var element in document.Elements)
                {
                    Tick();
                    if (!elements.TryAdd(element.NodePath, element)) return null;
                    if (element.ParentPath is null) roots.Add(element);
                    else
                    {
                        if (!reverseChildren.TryGetValue(element.ParentPath, out var children))
                            reverseChildren.Add(element.ParentPath, children = new(StringComparer.Ordinal));
                        children.Add(element.NodePath);
                    }
                }
                if (roots.Count != 1 || roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" }) return null;
                var identifiers = Children(roots[0], "study-identifier");
                var ids = identifiers.Count == 1 ? Children(identifiers[0], "study-id") : [];
                return ids.Count == 1 && ids[0].Children.Count == 0 && !reverseChildren.ContainsKey(ids[0].NodePath) &&
                    !string.IsNullOrWhiteSpace(ids[0].Text) ? ids[0].Text : null;

                List<ParsedXmlElement> Children(ParsedXmlElement parent, string name)
                {
                    var result = new List<ParsedXmlElement>();
                    var listed = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var path in parent.Children)
                    {
                        Tick();
                        if (!listed.Add(path) || !elements.TryGetValue(path, out var child) || child.ParentPath != parent.NodePath) return [];
                        if (child.Name == name && child.LocalName == name && child.NamespaceUri.Length == 0) result.Add(child);
                    }
                    if (reverseChildren.TryGetValue(parent.NodePath, out var actual))
                        foreach (var path in actual) { Tick(); if (!listed.Contains(path)) return []; }
                    return result;
                }
            }
        }
        catch (StudyContinuityWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Compared each actual current STF append owner with its exact direct modified-file predecessor using complete, uniquely bound study-id text. Other lifecycle operations and earlier historical edges are not reclassified; unavailable or contradictory evidence remains unevaluated.", findings);

        string? Explicit(ParsedXmlElement element, string name)
        {
            ParsedXmlAttributeValue? value = null;
            foreach (var attribute in element.Attributes.Values) { Tick(); if (attribute.Name == name && attribute.LocalName == name && attribute.NamespaceUri.Length == 0) value = attribute; }
            return value is { IsDefault: false } ? value.Value : null;
        }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new StudyContinuityWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class StudyContinuityWorkLimitException : Exception;
}
