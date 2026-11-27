using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaExtraneousHeadingAttributeRule
{
    internal const string RuleId = "FDA47-1368";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1368" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1368 requires the archived criteria and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1368.");
            yield break;
        }
        if (context.Assets.Digest is not ("71246a025b0a8cbb97a57e7eca16919a50cbd69c7fa573936656529f6d3d50a1" or
            "09f527b1a36fecda6a24bc58239ad7906f4d113346190ce2c9bc30425590f001") ||
            context.NodeSchema.Version != "ich-3.2.2-nodes-v1" ||
            context.NodeSchema.SourceSha256 != "c094aa2bded99564ade8ac78ae1540f95e518461b8eebe9a3063f67a165c2731")
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The selected ICH schema and asset manifest are not the qualified attribute source.");
            yield break;
        }
        var limits = target.Limits;
        var observations = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || observations.Length != 1 ||
            observations[0].RulesDigest != context.Catalog.Digest || observations[0].AssetManifestDigest != context.Assets.Digest ||
            observations[0].LimitsDigest != limits.Digest())
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Heading attributes require unique current XML bound to the capture, rules, assets and limits.");
            yield break;
        }
        var xml = observations[0];
        var incomplete = xml.Backbones.Count == 0 ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        var findings = new List<ValidationFinding>();
        var work = limits.MaxXmlNodes;
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in target.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0 || !files.Add(file.LogicalPath))
            {
                yield return new(RuleId, CheckStatus.NotEvaluated, "Captured file indexing is incomplete or ambiguous.");
                yield break;
            }
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in xml.Backbones)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0)
            {
                yield return new(RuleId, CheckStatus.NotEvaluated, "Backbone identity indexing exceeds the work limit.");
                yield break;
            }
            if (!paths.Add(document.LogicalPath)) duplicates.Add(document.LogicalPath);
        }
        var qualifiedBackbones = new List<ParsedBackbone>();
        foreach (var document in xml.Backbones.Where(item => item.Kind == BackboneKind.Ich))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!document.ReadComplete || document.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal) ||
                !files.Contains(document.LogicalPath) || duplicates.Contains(document.LogicalPath)) { incomplete = true; continue; }
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string?>(StringComparer.Ordinal);
            ParsedXmlElement? root = null;
            var unique = true;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                unique &= elements.TryAdd(element.NodePath, element);
                foreach (var child in element.Children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (work-- <= 0) { unique = false; break; }
                    if (!owners.TryAdd(child, element.NodePath)) owners[child] = null;
                }
                if (element.ParentPath is not null) continue;
                if (root is not null) unique = false;
                root = element;
            }
            if (!unique || root is not { LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } ||
                root.Location.LogicalPath != document.LogicalPath || root.Location.NodePath != root.NodePath || owners.ContainsKey(root.NodePath))
            { incomplete = true; continue; }
            var structurallyQualified = true;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { incomplete = true; structurallyQualified = false; break; }
                if (!Rooted(element, root, elements, owners, document.LogicalPath, limits.MaxXmlDepth, ref work, cancellationToken))
                { incomplete = true; structurallyQualified = false; continue; }
                if (element.NamespaceUri.Length != 0 || !context.NodeSchema.Definitions.TryGetValue(element.Name, out var definition) ||
                    definition.Kind != CtdNodeKind.Standard) continue;
                var placement = HasQualifiedAncestors(element, definition, elements, context.NodeSchema, root.NodePath, ref work, cancellationToken);
                if (placement is null) { incomplete = true; structurallyQualified = false; break; }
                if (placement == false) continue;
                foreach (var attribute in element.Attributes.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (work-- <= 0) { incomplete = true; structurallyQualified = false; break; }
                    if (attribute.IsDefault || attribute.NamespaceUri == "http://www.w3.org/2000/xmlns/" ||
                        definition.Attributes.Any(item => item.Name == attribute.Name)) continue;
                    if (attribute.ExplicitValue is null) { incomplete = true; continue; }
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "The target ICH heading supplies an attribute not declared for that heading in the pinned DTD.",
                        element.Location with { FieldPath = "@" + attribute.Name, Line = attribute.Line, Column = attribute.Column,
                            Files = [new(target.SourceId, document.LogicalPath)] }, Actual: attribute.Value,
                        RecommendedAction: "Review the heading attribute and the submission procedure against the pinned ICH DTD."));
                }
            }
            if (structurallyQualified) qualifiedBackbones.Add(document);
        }
        var content = FdaExtraneousContentAttributeRule.Inspect(target, lifecycle, context, rule, remaining, cancellationToken);
        findings.AddRange(content.Findings);
        remaining -= content.Findings.Count;
        incomplete |= content.Status == CheckStatus.NotEvaluated;
        var qualifiedXml = work < 0 ? null : new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId,
            xml.AssetManifestDigest, xml.LimitsDigest, qualifiedBackbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var stf = FdaStfExtraneousCategoryRule.Inspect(target, qualifiedXml, context, rule, remaining, cancellationToken);
        findings.AddRange(stf.Findings);
        incomplete |= stf.Status == CheckStatus.NotEvaluated;
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked explicit attributes on qualified standard ICH heading chains. " + content.Reason + " " + stf.Reason, findings);
    }

    private static bool Rooted(ParsedXmlElement element, ParsedXmlElement root, Dictionary<string, ParsedXmlElement> elements,
        Dictionary<string, string?> owners, string path, int maxDepth, ref int work, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0;
        while (element != root)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || ++depth > maxDepth || !seen.Add(element.NodePath) ||
                element.Location.LogicalPath != path || element.Location.NodePath != element.NodePath ||
                element.ParentPath is not { } parent || !elements.TryGetValue(parent, out var owner) ||
                !owners.TryGetValue(element.NodePath, out var declaredOwner) || declaredOwner != parent) return false;
            element = owner;
        }
        return true;
    }

    private static bool? HasQualifiedAncestors(ParsedXmlElement element, SectionDefinition definition,
        Dictionary<string, ParsedXmlElement> elements, SectionDefinitionSet schema, string rootPath, ref int work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (work-- <= 0) return null;
        while (definition.ParentDefinitionKey is { } parentKey)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) return null;
            if (element.ParentPath is not { } path || !elements.TryGetValue(path, out var parent) ||
                parent.NamespaceUri.Length != 0 || parent.Name != parentKey) return false;
            element = parent;
            definition = schema.Get(parentKey);
        }
        return element.ParentPath == rootPath;
    }
}
