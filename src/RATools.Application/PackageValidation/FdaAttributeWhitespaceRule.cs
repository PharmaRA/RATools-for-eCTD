using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaAttributeWhitespaceRule
{
    internal const string RuleId = "FDA47-1344";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1344" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1344 requires the archived criteria and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1344.");
            yield break;
        }
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var incomplete = xml is null || xml.Backbones.Count == 0 ||
            !xml.StudyTaggingFiles.InventoryComplete ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        var findings = new List<ValidationFinding>();
        var documents = (xml?.Backbones ?? []).Select(document => (document.LogicalPath, document.ReadComplete,
                document.Elements, Leaves: (IEnumerable<ParsedPackageLeaf>)document.Leaves))
            .Concat((xml?.StudyTaggingFiles.Documents ?? []).Select(document => (document.LogicalPath, document.ReadComplete,
                document.Elements, Leaves: Enumerable.Empty<ParsedPackageLeaf>())));
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.ReadComplete) { incomplete = true; continue; }
            var leaves = document.Leaves.ToDictionary(leaf => leaf.NodePath, StringComparer.Ordinal);
            foreach (var element in document.Elements)
            foreach (var attribute in element.Attributes.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attribute.IsDefault) continue;
                if (attribute.ExplicitValue is not { } value) { incomplete = true; continue; }
                if (!value.StartsWith(' ') && !value.EndsWith(' ')) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The explicitly supplied attribute value starts or ends with a space before DTD type normalization.",
                    element.Location with { FieldPath = "@" + attribute.Name, Line = attribute.Line, Column = attribute.Column,
                        LeafId = leaves.GetValueOrDefault(element.NodePath)?.Id, Files = [new(target.SourceId, document.LogicalPath)] },
                    Actual: value, RecommendedAction: "Review the source attribute value and the submission procedure for leading or trailing spaces."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked explicit target backbone and discovered STF attributes before DTD type normalization. Default attributes are excluded; other XML scopes remain unqualified.", findings);
    }
}
