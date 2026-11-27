using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRequiredHeadingAttributeRule
{
    internal const string RuleId = "FDA47-1357";
    private static readonly string[] SubstanceAttributes = ["substance", "manufacturer"];
    private static readonly string[] EfficacyAttributes = ["indication"];

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1357" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1357 requires the archived criteria and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1357.");
            yield break;
        }
        var profile = context.Assets.ForProfile(lifecycle.ProfileSnapshotId).Single(item => item.Kind == BackboneKind.Ich);
        if (context.Assets.Assets[profile.MainAssetId].Sha256 != "c094aa2bded99564ade8ac78ae1540f95e518461b8eebe9a3063f67a165c2731")
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The selected ICH DTD is not the pinned source for these required heading attributes.");
            yield break;
        }
        var limits = target.Limits;
        var xmls = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || xmls.Length != 1 ||
            xmls[0].RulesDigest != context.Catalog.Digest || xmls[0].AssetManifestDigest != context.Assets.Digest || xmls[0].LimitsDigest != limits.Digest())
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Required heading assessment needs unique XML bound to the current capture, rules, assets and limits.");
            yield break;
        }
        var xml = xmls[0];
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
                yield return new(RuleId, CheckStatus.NotEvaluated, "Captured file inventory is incomplete or ambiguous.");
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
                yield return new(RuleId, CheckStatus.NotEvaluated, "Backbone inventory exceeds the identity work limit.");
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
            if (!unique || root is null || root.LocalName != profile.RootLocalName || root.NamespaceUri != profile.NamespaceUri ||
                root.Location.LogicalPath != document.LogicalPath || root.Location.NodePath != root.NodePath || owners.ContainsKey(root.NodePath))
            { incomplete = true; continue; }
            var structurallyQualified = true;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { incomplete = true; structurallyQualified = false; break; }
                var required = RequiredAttributes(element, root, elements, owners, document.LogicalPath, limits.MaxXmlDepth, ref work, cancellationToken);
                if (required is null) { incomplete = true; structurallyQualified = false; continue; }
                foreach (var name in required)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (work-- <= 0) { incomplete = true; structurallyQualified = false; break; }
                    var attribute = element.Attributes.GetValueOrDefault(name);
                    if (!string.IsNullOrWhiteSpace(attribute?.Value)) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "The target ICH heading omits a nonblank required attribute value.",
                        element.Location with { FieldPath = "@" + name, Line = attribute?.Line ?? element.Location.Line,
                            Column = attribute?.Column ?? element.Location.Column, Files = [new(target.SourceId, document.LogicalPath)] },
                        Expected: "Nonblank " + name, Actual: attribute?.Value,
                        RecommendedAction: "Review the required heading attribute against the pinned ICH DTD."));
                }
            }
            if (structurallyQualified) qualifiedBackbones.Add(document);
        }
        // The category consumer has its own policy; only pass backbones whose node
        // identity and parent graph were qualified here, never conflicting raw facts.
        var qualifiedXml = work < 0 ? null : new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId,
            xml.AssetManifestDigest, xml.LimitsDigest, qualifiedBackbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var stf = FdaStfRequiredCategoryRule.Inspect(target, qualifiedXml, context, rule, remaining, cancellationToken);
        findings.AddRange(stf.Findings);
        incomplete |= stf.Status == CheckStatus.NotEvaluated;
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked required attributes in the four qualified ICH heading chains. " + stf.Reason, findings);
    }

    private static string[]? RequiredAttributes(ParsedXmlElement element, ParsedXmlElement root,
        Dictionary<string, ParsedXmlElement> elements, Dictionary<string, string?> owners, string path,
        int maxDepth, ref int work, CancellationToken token)
    {
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = element;
        var plain = true;
        while (current != root)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || chain.Count >= maxDepth || !seen.Add(current.NodePath) ||
                current.Location.LogicalPath != path || current.Location.NodePath != current.NodePath ||
                current.ParentPath is not { } parent || !elements.TryGetValue(parent, out var owner) ||
                !owners.TryGetValue(current.NodePath, out var declaredOwner) || declaredOwner != parent) return null;
            plain &= current.NamespaceUri.Length == 0;
            chain.Add(current.LocalName);
            current = owner;
        }
        if (!plain) return [];
        return chain switch
        {
            ["m2-3-s-drug-substance", "m2-3-quality-overall-summary", "m2-common-technical-document-summaries"] => SubstanceAttributes,
            ["m3-2-s-drug-substance", "m3-2-body-of-data", "m3-quality"] => SubstanceAttributes,
            ["m2-7-3-summary-of-clinical-efficacy", "m2-7-clinical-summary", "m2-common-technical-document-summaries"] => EfficacyAttributes,
            ["m5-3-5-reports-of-efficacy-and-safety-studies", "m5-3-clinical-study-reports", "m5-clinical-study-reports"] => EfficacyAttributes,
            _ => []
        };
    }
}
