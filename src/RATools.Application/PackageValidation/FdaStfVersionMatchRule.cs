using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfVersionMatchRule
{
    internal const string RuleId = "FDA47-1765";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF version comparison did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1765" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1765 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1765.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured STF specification is required.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var seen = new HashSet<LeafAddress>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var indices = new List<ParsedBackbone>();
            var observations = new Dictionary<(string Digest, string Path), int>();
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                foreach (var stf in xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    var key = (xml.InputDigest, stf.LogicalPath);
                    observations[key] = observations.GetValueOrDefault(key) + 1;
                }
                if (xml.InputDigest != packages.Target.InputDigest || xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                    xml.RulesDigest != context.Catalog.Digest || xml.AssetManifestDigest != context.Assets.Digest ||
                    xml.LimitsDigest != packages.Target.Limits.Digest()) continue;
                foreach (var document in xml.Backbones)
                {
                    Tick();
                    if (document.Kind == BackboneKind.Ich && document.LogicalPath == packages.Target.Manifest.SequenceNumber + "/index.xml") indices.Add(document);
                }
            }
            if (indices.Count != 1) return Unknown("A unique frozen target ICH backbone is required.");
            var index = indices[0];
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var element in index.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element)) return Unknown("Target element identities are ambiguous.");
                foreach (var attribute in element.Attributes.Values)
                {
                    Tick();
                    if (attribute.Name is "ID" or "xml:id") ids[attribute.Value] = ids.GetValueOrDefault(attribute.Value) + 1;
                }
            }
            var stfs = new Dictionary<string, BoundStudyTaggingFile>(StringComparer.Ordinal);
            foreach (var document in bindings.Documents)
            {
                Tick();
                if (!stfs.TryAdd(document.Document.LogicalPath, document)) return Unknown("STF identities are ambiguous.");
            }
            foreach (var element in elements.Values)
            {
                Tick();
                if (!Plain(element, "leaf") || Explicit(element, "operation", "") == "delete") continue;
                var href = Explicit(element, "href", "http://www.w3c.org/1999/xlink");
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, href!); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || !stfs.TryGetValue(path, out var bound)) continue;
                var document = bound.Document;
                var roots = new List<ParsedXmlElement>();
                foreach (var observed in document.Elements) { Tick(); if (observed.ParentPath is null) roots.Add(observed); }
                var id = Explicit(element, "ID", "");
                LeafAddress address;
                try { address = new(packages.Target.Manifest.ApplicationId, packages.Target.Manifest.SequenceNumber, "index.xml", id!); }
                catch (Exception error) when (error is ArgumentException or System.Xml.XmlException) { incomplete = true; continue; }
                // Retained complete start tags can prove a local mismatch even when a later
                // parse error prevents the lifecycle layer from publishing any leaf events.
                if (roots.Count != 1 || roots[0] is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" } ||
                    ids.GetValueOrDefault(id!) != 1 || !QualifiedHeading(element) ||
                    Explicit(element, "operation", "") is not ("new" or "append" or "replace") ||
                    reference.Query is not null || reference.Fragment is not null || observations.GetValueOrDefault((bound.InputDigest, path)) != 1)
                { incomplete = true; continue; }
                applicable = true;
                incomplete |= !document.ReadComplete || document.DtdStatus != CheckStatus.Pass;
                if (!seen.Add(address)) continue;
                var selected = context.ForSequence(path.Split('/')[0]);
                if (!selected.Assets.Assets.TryGetValue("stf-dtd", out var asset) || asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f" ||
                    !document.ResolvedAssets.Contains("stf-dtd")) { incomplete = true; continue; }
                var root = roots[0];
                var rootVersion = root.Attributes.GetValueOrDefault("dtd-version");
                var ownerVersion = element.Attributes.GetValueOrDefault("version");
                if (rootVersion is not { LocalName: "dtd-version", NamespaceUri.Length: 0, Value: "2.2" } ||
                    ownerVersion is not { LocalName: "version", NamespaceUri.Length: 0, IsDefault: false } ||
                    VersionToken(ownerVersion.Value) is not { } token)
                { incomplete = true; continue; }
                if (token == rootVersion.Value) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The STF version declared by this leaf differs from the effective DTD version of its referenced STF.",
                    element.Location with { LeafId = id, FieldPath = "@version", Files = [new(packages.Target.SourceId, index.LogicalPath), new(bound.SourceId, path)] },
                    Expected: rootVersion.Value, Actual: ownerVersion.Value));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An STF owner/root version mismatch is established; other pairs may remain unqualified.", findings.ToArray()));
            }

            bool QualifiedHeading(ParsedXmlElement current)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                ParsedXmlElement? Parent()
                {
                    Tick();
                    if (!visited.Add(current.NodePath) || current.ParentPath is not { } parentPath || !elements.TryGetValue(parentPath, out var parent)) return null;
                    var contains = false;
                    foreach (var child in parent.Children) { Tick(); if (child == current.NodePath) contains = true; }
                    if (!contains) return null;
                    current = parent;
                    return parent;
                }
                var heading = Parent();
                while (heading is not null && Plain(heading, "node-extension")) heading = Parent();
                if (heading is null || heading.NamespaceUri.Length != 0 || !context.NodeSchema.Definitions.TryGetValue(heading.Name, out var definition)) return false;
                while (definition.ParentDefinitionKey is { } parentKey)
                {
                    var parent = Parent();
                    if (parent is null || !Plain(parent, parentKey)) return false;
                    definition = context.NodeSchema.Get(parentKey);
                }
                return Parent() is { ParentPath: null, LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" };
            }
        }
        catch (VersionMatchWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Compared actual target owner declarations with their corresponding frozen STF root versions, including selected historical content. Known DTD defaults are effective versions, not explicit declarations. Unknown syntax/profile facts remain unevaluated; omission and supported declaration format belong to separate rules.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new VersionMatchWorkLimitException(); }
    }

    private static string? VersionToken(string value)
    {
        // These spellings occur in the archived specification and FDA 1782 correction text.
        // Interpretation for comparison does not approve the declaration format under 1782.
        if (!(value.StartsWith("stf version ", StringComparison.Ordinal) || value.StartsWith("STF version ", StringComparison.Ordinal) ||
            value.StartsWith("STF Version ", StringComparison.Ordinal)) || value.Length != 15) return null;
        var token = value[12..];
        return token[0] is >= '0' and <= '9' && token[1] == '.' && token[2] is >= '0' and <= '9' ? token : null;
    }
    private static string? Explicit(ParsedXmlElement element, string name, string ns) => element.Attributes.Values
        .SingleOrDefault(attribute => attribute.LocalName == name && attribute.NamespaceUri == ns && !attribute.IsDefault)?.Value;
    private static bool Plain(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class VersionMatchWorkLimitException : Exception;
}
