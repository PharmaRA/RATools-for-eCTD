using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaExtraneousLeafPathRules
{
    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var skippedRegional = xml is not null && HasSkippedRegional(target, xml, cancellationToken);
        foreach (var rule in context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).Where(rule => rule.InternalRuleId is "FDA47-1051" or "FDA47-1068" or "FDA47-1136" or "FDA47-1170"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authorized = rule.AuthorityRuleId == rule.InternalRuleId[6..] && rule.RuleVersion == "4.7.1" &&
                context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
                source.Kind == RuleSourceKind.RegulatorySpecification && source.Version == "4.7" && source.VerificationStatus == "Verified" &&
                source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" && context.HasArchivedOfficialSource(source);
            if (!authorized || context.SelectionDate is null)
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotEvaluated, "This FDA path rule requires the exact archived official source and explicit selection date.");
                continue;
            }
            if (context.SelectionDate < new DateOnly(2008, 3, 10))
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotApplicable, "The selected date precedes this FDA path rule's effective date.");
                continue;
            }
            var incomplete = skippedRegional || xml is null || xml.Backbones.Count == 0 ||
                !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
                !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass);
            var findings = new List<ValidationFinding>();
            var required = rule.InternalRuleId is "FDA47-1136" or "FDA47-1170";
            var hrefRule = rule.InternalRuleId is "FDA47-1051" or "FDA47-1136";
            var fieldName = hrefRule ? "xlink:href" : "modified-file";
            foreach (var document in xml?.Backbones ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!document.ReadComplete) { incomplete = true; continue; }
                var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
                foreach (var leaf in document.Leaves)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var element = elements[leaf.NodePath];
                    var operation = element.Attributes.GetValueOrDefault("operation");
                    if (operation is null || operation.IsDefault || operation.Value is not ("new" or "append" or "replace" or "delete"))
                    { incomplete = true; continue; }
                    var needsPath = hrefRule ? operation.Value != "delete" : operation.Value != "new";
                    if (needsPath != required) continue;
                    var attribute = hrefRule ? element.Attributes.Values.FirstOrDefault(value =>
                        value.LocalName == "href" && value.NamespaceUri == "http://www.w3c.org/1999/xlink") : element.Attributes.GetValueOrDefault("modified-file");
                    if (required ? !string.IsNullOrWhiteSpace(attribute?.Value) : string.IsNullOrEmpty(attribute?.Value)) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    var location = leaf.Location with { FieldPath = "@" + (attribute?.Name ?? fieldName),
                        Line = attribute?.Line is > 0 ? attribute.Line : leaf.Location.Line,
                        Column = attribute?.Column is > 0 ? attribute.Column : leaf.Location.Column,
                        Files = [new(target.SourceId, document.LogicalPath)] };
                    findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                        required ? $"The {operation.Value} leaf requires a {fieldName} path." : $"The {operation.Value} leaf supplies an extraneous {fieldName} path.",
                        location, Actual: attribute?.Value, RecommendedAction: required ? $"Provide the {fieldName} path specified by {rule.SourceSection}." : $"Omit the extraneous {fieldName} path identified by {rule.SourceSection}."));
                }
            }
            yield return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
                incomplete ? "Some target leaf paths remain unevaluated due to incomplete XML, unknown operations or the shared finding budget." :
                    "Checked target backbone path values against explicitly supplied operations; reference validity and lifecycle targets are assessed separately.", findings);
        }
    }

    internal static bool HasSkippedRegional(ICapturedPackageInput target, PackageXmlInspection xml, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in xml.Checks.SelectMany(check => check.Findings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finding.Code != "INVALID_REGIONAL_BACKBONE_REFERENCE" || finding.Location.LogicalPath is not { } source || finding.Actual is not { } value) continue;
            try
            {
                var path = PackageLogicalPath.ResolveReference(source, value).LogicalPath;
                if (path is not null && path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) candidates.Add(path);
            }
            catch (ArgumentException) { continue; }
        }
        if (candidates.Count == 0) return false;
        candidates.ExceptWith(xml.Backbones.Select(document => document.LogicalPath));
        return candidates.Count > 0 && target.Manifest.Files.Any(file => candidates.Contains(file.LogicalPath));
    }
}
