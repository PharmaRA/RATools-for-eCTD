using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaLeafOperationRules
{
    internal const string RuleId = "FDA47-1034";

    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(rule => rule.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = rule.AuthorityRuleId == "1034" && rule.RuleVersion == "4.7.1" &&
            context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
            source.Kind == RuleSourceKind.RegulatorySpecification && source.Version == "4.7" && source.VerificationStatus == "Verified" &&
            source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" && context.HasArchivedOfficialSource(source);
        if (!authorized || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1034 requires the exact archived official source and explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 1034's effective date.");
            yield break;
        }
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var incomplete = xml is null || xml.Backbones.Count == 0 ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass);
        var findings = new List<ValidationFinding>();
        foreach (var document in xml?.Backbones ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.ReadComplete) { incomplete = true; continue; }
            var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attribute = elements[leaf.NodePath].Attributes.GetValueOrDefault("operation");
                if (attribute is { IsDefault: false } && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    if (attribute.Value is not ("new" or "append" or "replace" or "delete")) incomplete = true;
                    continue;
                }
                if (remaining == 0) { incomplete = true; continue; }
                remaining--;
                var location = leaf.Location with { FieldPath = "@operation",
                    Line = attribute?.Line is > 0 ? attribute.Line : leaf.Location.Line,
                    Column = attribute?.Column is > 0 ? attribute.Column : leaf.Location.Column,
                    Files = [new(target.SourceId, document.LogicalPath)] };
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The leaf has no explicitly supplied nonblank operation; a default value cannot establish the intended lifecycle operation.",
                    location, Actual: attribute?.Value,
                    RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; supply the intended operation without automatically substituting new."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            incomplete ? "Some operations remain unevaluated due to incomplete XML, unsupported values or the shared finding budget." :
                "Checked explicitly supplied operations in target-sequence backbones; other lifecycle validity remains separately evaluated.", findings);
    }
}
