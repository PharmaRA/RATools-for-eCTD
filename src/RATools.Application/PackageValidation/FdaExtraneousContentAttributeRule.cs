using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaExtraneousContentAttributeRule
{
    internal static RuleCheckResult Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, ValidationRuleDescriptor rule, int remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var limits = target.Limits;
        var observations = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || observations.Length != 1 ||
            observations[0].RulesDigest != context.Catalog.Digest || observations[0].AssetManifestDigest != context.Assets.Digest ||
            observations[0].LimitsDigest != limits.Digest() || !context.Assets.Assets.TryGetValue("ich-dtd", out var dtd) ||
            dtd.Sha256 != "c094aa2bded99564ade8ac78ae1540f95e518461b8eebe9a3063f67a165c2731")
            return new(rule.InternalRuleId, CheckStatus.NotEvaluated, "Content attributes require unique current XML facts and the exact ICH DTD, rules, assets and limits.");

        var findings = new List<ValidationFinding>();
        var incomplete = false;
        var work = limits.MaxXmlNodes;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var duplicatePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in observations[0].Backbones)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!paths.Add(document.LogicalPath)) duplicatePaths.Add(document.LogicalPath);
        }
        foreach (var document in observations[0].Backbones)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (document.Kind != BackboneKind.Ich) continue;
            if (!document.ReadComplete || document.ProfileSnapshotId != lifecycle.ProfileSnapshotId || duplicatePaths.Contains(document.LogicalPath) ||
                !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal))
            { incomplete = true; continue; }
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var roots = 0; ParsedXmlElement? root = null; var unique = true;
            foreach (var element in document.Elements)
            {
                token.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != document.LogicalPath || element.Location.NodePath != element.NodePath) unique = false;
                if (element.ParentPath is null) { roots++; root = element; }
            }
            if (!unique || roots != 1 || root is not { LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" })
            { incomplete = true; continue; }
            foreach (var element in document.Elements)
            {
                token.ThrowIfCancellationRequested();
                if (work-- <= 0) { incomplete = true; break; }
                if (element.NamespaceUri.Length != 0 || element.LocalName is not ("leaf" or "node-extension")) continue;
                if (!QualifiedPlacement(element, root, elements, context.NodeSchema, limits.MaxXmlDepth, ref work, token))
                { incomplete = true; continue; }
                foreach (var attribute in element.Attributes.Values)
                {
                    token.ThrowIfCancellationRequested();
                    if (work-- <= 0) { incomplete = true; break; }
                    if (attribute.IsDefault || attribute.NamespaceUri == "http://www.w3.org/2000/xmlns/") continue;
                    if (attribute.ExplicitValue is null) { incomplete = true; continue; }
                    if (Allowed(element.LocalName, attribute)) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                        "The qualified ICH leaf or node-extension supplies an attribute outside its pinned DTD declaration.",
                        element.Location with { FieldPath = "@" + attribute.Name, Line = attribute.Line, Column = attribute.Column,
                            LeafId = element.LocalName == "leaf" && element.Attributes.GetValueOrDefault("ID") is { LocalName: "ID", NamespaceUri: "" } id ? id.Value : null,
                            Files = [new(target.SourceId, document.LogicalPath)] }, Actual: attribute.Value,
                        RecommendedAction: "Review the attribute against the ICH leaf or node-extension declaration; regional content is assessed separately."));
                }
            }
        }
        return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked explicit attributes of qualified current ICH leaves and node-extensions; regional and other content scopes remain unqualified.", findings);
    }

    private static bool Allowed(string element, ParsedXmlAttributeValue attribute)
    {
        if (attribute.NamespaceUri == "http://www.w3.org/XML/1998/namespace") return attribute.LocalName == "lang";
        if (attribute.NamespaceUri.Length == 0)
            return attribute.LocalName == "ID" || element == "leaf" && attribute.LocalName is
                "application-version" or "version" or "font-library" or "operation" or "modified-file" or "checksum" or "checksum-type" or "keywords";
        return element == "leaf" && attribute.NamespaceUri == "http://www.w3c.org/1999/xlink" &&
            attribute.LocalName is "type" or "role" or "href" or "show" or "actuate";
    }

    private static bool QualifiedPlacement(ParsedXmlElement element, ParsedXmlElement root, Dictionary<string, ParsedXmlElement> elements,
        SectionDefinitionSet schema, int maxDepth, ref int work, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { element.NodePath };
        var childKind = element.LocalName;
        var depth = 0;
        while (element.ParentPath is { } path)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || ++depth > maxDepth || !seen.Add(path) || !elements.TryGetValue(path, out element!)) return false;
            if (element.NamespaceUri.Length != 0) return false;
            if (element.LocalName == "node-extension") { childKind = "node-extension"; continue; }
            if (!schema.Definitions.TryGetValue(element.LocalName, out var definition) || definition.Kind != CtdNodeKind.Standard ||
                (childKind == "leaf" ? !definition.AllowsLeaves : definition.ExtensionPolicy != NodeExtensionPolicy.Allowed)) return false;
            while (definition.ParentDefinitionKey is { } parentKey)
            {
                token.ThrowIfCancellationRequested();
                if (work-- <= 0 || ++depth > maxDepth || element.ParentPath is not { } parent || !seen.Add(parent) ||
                    !elements.TryGetValue(parent, out element!) || element.NamespaceUri.Length != 0 || element.LocalName != parentKey) return false;
                definition = schema.Get(parentKey);
            }
            token.ThrowIfCancellationRequested();
            return work-- > 0 && ++depth <= maxDepth && element.ParentPath == root.NodePath;
        }
        return false;
    }
}
