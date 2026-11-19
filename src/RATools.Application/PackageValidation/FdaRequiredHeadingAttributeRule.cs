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
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var incomplete = xml is null || xml.Backbones.Count == 0 ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        var findings = new List<ValidationFinding>();
        foreach (var document in xml?.Backbones.Where(item => item.Kind == BackboneKind.Ich) ?? [])
        {
            if (!document.ReadComplete) { incomplete = true; continue; }
            var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var name in RequiredAttributes(element, elements))
                {
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
        }
        var stf = FdaStfRequiredCategoryRule.Inspect(target, xml, context, rule, remaining, cancellationToken);
        findings.AddRange(stf.Findings);
        incomplete |= stf.Status == CheckStatus.NotEvaluated;
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked required attributes in the four qualified ICH heading chains. " + stf.Reason, findings);
    }

    private static string[] RequiredAttributes(ParsedXmlElement element, Dictionary<string, ParsedXmlElement> elements)
    {
        var chain = new List<string>();
        var current = element;
        while (current.ParentPath is { } parent)
        {
            if (current.NamespaceUri.Length != 0) return [];
            chain.Add(current.LocalName);
            current = elements[parent];
        }
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
