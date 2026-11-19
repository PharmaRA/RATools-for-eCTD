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
                if (element.NamespaceUri.Length != 0 || !context.NodeSchema.Definitions.TryGetValue(element.Name, out var definition) ||
                    definition.Kind != CtdNodeKind.Standard || !HasQualifiedAncestors(element, definition, elements, context.NodeSchema, document.Elements[0].NodePath)) continue;
                foreach (var attribute in element.Attributes.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attribute.IsDefault || attribute.NamespaceUri == "http://www.w3.org/2000/xmlns/" ||
                        definition.Attributes.Any(item => item.Name == attribute.Name)) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "The target ICH heading supplies an attribute not declared for that heading in the pinned DTD.",
                        element.Location with { FieldPath = "@" + attribute.Name, Line = attribute.Line, Column = attribute.Column,
                            Files = [new(target.SourceId, document.LogicalPath)] }, Actual: attribute.Value,
                        RecommendedAction: "Review the heading attribute and the submission procedure against the pinned ICH DTD."));
                }
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked explicit attributes on qualified standard ICH heading chains. Regional, leaf, extension and STF attribute coverage remains unqualified.", findings);
    }

    private static bool HasQualifiedAncestors(ParsedXmlElement element, SectionDefinition definition,
        Dictionary<string, ParsedXmlElement> elements, SectionDefinitionSet schema, string rootPath)
    {
        while (definition.ParentDefinitionKey is { } parentKey)
        {
            if (element.ParentPath is not { } path || !elements.TryGetValue(path, out var parent) ||
                parent.NamespaceUri.Length != 0 || parent.Name != parentKey) return false;
            element = parent;
            definition = schema.Get(parentKey);
        }
        return element.ParentPath == rootPath;
    }
}
