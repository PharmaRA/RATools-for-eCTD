using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaLeafChecksumAttributeRules
{
    internal const string TypeRuleId = "FDA47-1408";
    internal const string RequiredRuleId = "FDA47-1425";
    internal const string DeleteRuleId = "FDA47-1426";
    private static readonly ValidationRuleSource IchSpecification = new("ich-specification", RuleSourceKind.RegulatorySpecification, "3.2.2",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf",
        "d8273833eac401ff3c094d782863b078110e33a7f358438d3f7a0dad277fc2b0", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        foreach (var rule in context.Catalog.ForProfile(lifecycle.ProfileSnapshotId)
            .Where(item => item.InternalRuleId is TypeRuleId or RequiredRuleId or DeleteRuleId).OrderBy(item => item.InternalRuleId, StringComparer.Ordinal))
        {
            retainProgress?.Invoke(new(rule.InternalRuleId, CheckStatus.NotEvaluated, "The checksum attribute inspection did not complete."));
            cancellationToken.ThrowIfCancellationRequested();
            if (rule.AuthorityRuleId != rule.InternalRuleId[6..] || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotEvaluated, "The checksum attribute rule requires the archived FDA criteria and a selection date.");
                continue;
            }
            var deleteRule = rule.InternalRuleId == DeleteRuleId;
            if (context.SelectionDate < (deleteRule ? new DateOnly(2013, 4, 30) : new DateOnly(2008, 3, 10)))
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotApplicable, "The selection precedes the rule's effective date.");
                continue;
            }
            if (!context.HasArchivedOfficialSource(IchSpecification))
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotEvaluated, "The selected snapshot requires the archived ICH checksum and operation specification.");
                continue;
            }
            var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
            var incomplete = xml is null || xml.Backbones.Count == 0 ||
                !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
                !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
                xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
            var findings = new List<ValidationFinding>();
            foreach (var document in xml?.Backbones ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!document.ReadComplete || document.Kind is not (BackboneKind.Ich or BackboneKind.UsRegional)) { incomplete = true; continue; }
                var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
                foreach (var leaf in document.Leaves)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var element = elements[leaf.NodePath];
                    var typeRule = rule.InternalRuleId == TypeRuleId;
                    var field = typeRule ? "checksum-type" : "checksum";
                    var attribute = element.Attributes.GetValueOrDefault(field);
                    if (typeRule)
                    {
                        if (attribute is null || attribute.IsDefault) { incomplete = true; continue; }
                        if (string.Equals(attribute.Value, "md5", StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    else
                    {
                        var operation = element.Attributes.GetValueOrDefault("operation");
                        if (operation is not { IsDefault: false, Value: "new" or "append" or "replace" or "delete" }) { incomplete = true; continue; }
                        if ((operation.Value == "delete") != deleteRule) continue;
                        if (attribute?.IsDefault == true) { incomplete = true; continue; }
                        if (deleteRule ? string.IsNullOrEmpty(attribute?.Value) : !string.IsNullOrWhiteSpace(attribute?.Value)) continue;
                    }
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                        typeRule ? "The supplied checksum algorithm is not MD5." : deleteRule ? "The delete leaf supplies a nonempty checksum value." : "The content leaf omits a nonblank checksum value.",
                        leaf.Location with { FieldPath = "@" + field, Line = attribute?.Line ?? leaf.Location.Line, Column = attribute?.Column ?? leaf.Location.Column,
                            Files = [new(target.SourceId, document.LogicalPath)] }, Expected: typeRule ? "MD5" : deleteRule ? "Empty checksum" : "Nonblank checksum", Actual: attribute?.Value,
                        RecommendedAction: "Review the supplied checksum attribute against the ICH specification."));
                    retainProgress?.Invoke(new(rule.InternalRuleId, CheckStatus.Fail, "A checksum attribute failure is established; remaining leaves may be incomplete.", findings.ToArray()));
                }
            }
            yield return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
                "Checked target ICH/US leaf checksum attributes. Missing evidence, unread backbones and unsupported XML scopes remain unevaluated.", findings);
        }
    }
}
