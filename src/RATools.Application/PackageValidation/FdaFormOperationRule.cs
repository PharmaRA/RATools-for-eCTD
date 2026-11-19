using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaFormOperationRule
{
    internal const string RuleId = "FDA47-1038";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1038" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1038 requires the archived criteria and the selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2013, 4, 30))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1038.");
            yield break;
        }

        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var regional = xml?.Backbones.Where(item => item.Kind == BackboneKind.UsRegional).ToArray() ?? [];
        var incomplete = regional.Length == 0 || xml!.Checks.Any(check =>
            (check.RuleId is "XML-OFFLINE" or "XML-LIMITS") && check.Status != CheckStatus.Pass ||
            check.Findings.Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE"));
        var findings = new List<ValidationFinding>();
        var failed = false;
        foreach (var document in regional)
        {
            if (!document.ReadComplete) { incomplete = true; continue; }
            var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsFormOrCover(leaf, elements)) continue;
                var operation = elements[leaf.NodePath].Attributes.GetValueOrDefault("operation");
                if (operation is not { IsDefault: false, Value: "new" or "replace" or "delete" or "append" }) { incomplete = true; continue; }
                if (operation.Value == "new") continue;
                var status = operation.Value == "replace" ? CheckStatus.NotEvaluated : CheckStatus.Fail;
                if (status == CheckStatus.NotEvaluated) incomplete = true;
                if (findings.Count >= remaining)
                {
                    var pending = status == CheckStatus.Fail ? findings.FindLastIndex(item => item.CheckStatus == CheckStatus.NotEvaluated) : -1;
                    if (pending < 0) { incomplete = true; continue; }
                    findings.RemoveAt(pending);
                }
                failed |= status == CheckStatus.Fail;
                findings.Add(new(RuleId, status, rule.Severity, RuleId, "The submitted application form or cover letter uses a lifecycle operation other than new.",
                    leaf.Location with { FieldPath = "@operation", Line = operation.Line, Column = operation.Column, Files = [new(target.SourceId, document.LogicalPath)] },
                    Expected: "new", Actual: operation.Value, RecommendedAction: status == CheckStatus.NotEvaluated
                        ? "Review whether this replacement corrects an incorrect document, as described in FDA 1038."
                        : "Review the submission procedure for providing forms and cover letters with operation new."));
            }
        }
        yield return new(RuleId, failed ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Assessed explicit operations on target US application forms and cover letters. Replacement exceptions require review.", findings);
    }

    private static bool IsFormOrCover(ParsedPackageLeaf leaf, IReadOnlyDictionary<string, ParsedXmlElement> elements)
    {
        var parentPath = elements[leaf.NodePath].ParentPath;
        while (parentPath is not null && elements[parentPath].Name == "node-extension" && elements[parentPath].NamespaceUri.Length == 0)
            parentPath = elements[parentPath].ParentPath;
        if (parentPath is null) return false;
        var chain = new List<string>();
        var current = elements[parentPath];
        while (current.ParentPath is { } ancestor)
        {
            if (current.NamespaceUri.Length != 0) return false;
            chain.Add(current.LocalName);
            current = elements[ancestor];
        }
        return chain is ["m1-2-cover-letters", "m1-regional"] or ["form", "m1-1-forms", "m1-regional"] or
            ["form", "submission-information", "application", "application-set", "admin"];
    }
}
