using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaInvalidLeafPathRule
{
    internal const string RuleId = "FDA47-1102";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        const string unknown = "Only explicit local target backbone path defects before the first query/fragment delimiter are qualified. Query/fragment validity, URI schemes/encoding, other XML scopes and historical sources remain unevaluated; no Pass is emitted.";
        if (rule.AuthorityRuleId != "1102" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        { yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1102 requires the exact archived criteria and an explicit selection date."); yield break; }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        { yield return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 1102."); yield break; }
        var limits = target.Limits;
        var xmls = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || xmls.Length != 1 ||
            xmls[0].RulesDigest != context.Catalog.Digest || xmls[0].AssetManifestDigest != context.Assets.Digest || xmls[0].LimitsDigest != limits.Digest())
        { yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1102 requires one exact target XML observation bound to rules, assets and limits."); yield break; }

        var findings = new List<ValidationFinding>();
        var work = limits.MaxXmlNodes;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in xmls[0].Backbones)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) break;
            if (!paths.Add(document.LogicalPath)) duplicates.Add(document.LogicalPath);
        }
        foreach (var document in xmls[0].Backbones)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) break;
            if (!document.ReadComplete || document.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal) || duplicates.Contains(document.LogicalPath) ||
                document.Kind is not (BackboneKind.Ich or BackboneKind.UsRegional)) continue;
            var profile = context.Assets.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.Kind == document.Kind);
            if (profile is null) continue;
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var unique = true; var roots = 0; ParsedXmlElement? root = null;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                if (!elements.TryAdd(element.NodePath, element)) unique = false;
                if (element.ParentPath is null) { roots++; root = element; }
            }
            if (!unique || roots != 1 || root is null || root.LocalName != profile.RootLocalName || root.NamespaceUri != profile.NamespaceUri) continue;
            var leafPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) break;
                if (!leafPaths.Add(leaf.NodePath)) { unique = false; break; }
            }
            if (!unique) continue;
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0 || findings.Count >= remaining) break;
                if (leaf.Location.LogicalPath != document.LogicalPath || leaf.Location.NodePath != leaf.NodePath ||
                    !elements.TryGetValue(leaf.NodePath, out var element) || element.LocalName != "leaf" || element.NamespaceUri.Length != 0 ||
                    element.ParentPath != leaf.ParentNodePath || !Rooted(element, root, elements, limits.MaxXmlDepth, ref work, cancellationToken)) continue;
                var operation = element.Attributes.GetValueOrDefault("operation");
                if (operation is not { LocalName: "operation", NamespaceUri: "", IsDefault: false, Value: "new" or "append" or "replace" } ||
                    operation.ExplicitValue != operation.Value || leaf.Operation != operation.Value) continue;
                ParsedXmlAttributeValue? href = null; var count = 0;
                foreach (var attribute in element.Attributes.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (work-- <= 0) { count = 0; break; }
                    if (attribute.LocalName == "href" && attribute.NamespaceUri == "http://www.w3c.org/1999/xlink") { href = attribute; count++; }
                }
                if (count != 1 || href is not { IsDefault: false, ExplicitValue: { } value } ||
                    value != href.Value || leaf.Href != value || !HasQualifiedDefect(value)) continue;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "An explicitly supplied local leaf path contains a character prohibited by FDA 1102.",
                    leaf.Location with { FieldPath = "@" + href.Name, Line = href.Line, Column = href.Column,
                        Files = [new(target.SourceId, document.LogicalPath)] }, Actual: value,
                    RecommendedAction: "Review the leaf path and correct the reference according to FDA 1102; URI semantics are assessed separately."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated, unknown, findings);
    }

    private static bool HasQualifiedDefect(string value)
    {
        // Do not mistake URI scheme, query/fragment text or encoded bytes for a file path.
        // RFC 3986 3.3: the first '?' or '#' ends the path; suffix contents cannot
        // erase an independently known path defect or supply a path defect themselves.
        var boundary = value.IndexOfAny(['?', '#']);
        if (boundary >= 0) value = value[..boundary];
        if (value.Length == 0 || value.StartsWith('/') || value.Contains('%')) return false;
        var colon = value.IndexOf(':'); var slash = value.IndexOf('/');
        if (colon >= 0 && (slash < 0 || colon < slash)) return false;
        return value.IndexOfAny(['\\', ':', '*', '<', '>', '|', ' ']) >= 0;
    }

    private static bool Rooted(ParsedXmlElement element, ParsedXmlElement root, Dictionary<string, ParsedXmlElement> elements,
        int maxDepth, ref int work, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { element.NodePath }; var depth = 0;
        while (element.ParentPath is { } parent)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || ++depth > maxDepth || !seen.Add(parent) || !elements.TryGetValue(parent, out element!)) return false;
            if (element == root) return true;
            if (element.NamespaceUri.Length != 0) return false;
        }
        return false;
    }
}
