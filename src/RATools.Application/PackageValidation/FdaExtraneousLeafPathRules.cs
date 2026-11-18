using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaExtraneousLeafPathRules
{
    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        foreach (var rule in context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).Where(rule => rule.InternalRuleId is "FDA47-1051" or "FDA47-1068"))
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
            var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
            var incomplete = xml is null || xml.Backbones.Count == 0 ||
                !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
                !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass);
            var findings = new List<ValidationFinding>();
            var hrefRule = rule.InternalRuleId == "FDA47-1051";
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
                    if (operation.Value != (hrefRule ? "delete" : "new")) continue;
                    var attribute = hrefRule ? element.Attributes.Values.FirstOrDefault(value =>
                        value.LocalName == "href" && value.NamespaceUri == "http://www.w3c.org/1999/xlink") : element.Attributes.GetValueOrDefault("modified-file");
                    if (attribute is null || attribute.Value.Length == 0) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    var location = leaf.Location with { FieldPath = "@" + attribute.Name,
                        Line = attribute.Line > 0 ? attribute.Line : leaf.Location.Line,
                        Column = attribute.Column > 0 ? attribute.Column : leaf.Location.Column,
                        Files = [new(target.SourceId, document.LogicalPath)] };
                    findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                        hrefRule ? "A delete leaf supplies an extraneous content path." : "A new leaf supplies an extraneous modified-file path.",
                        location, Actual: attribute.Value, RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; omit the path that this operation does not use."));
                }
            }
            yield return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
                incomplete ? "Some target leaf paths remain unevaluated due to incomplete XML, unknown operations or the shared finding budget." :
                    "Checked target backbone path values against explicitly supplied operations; reference validity and lifecycle targets are assessed separately.", findings);
        }
    }
}
