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

        var limits = target.Limits;
        var observations = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || observations.Length != 1 ||
            observations[0].RulesDigest != context.Catalog.Digest || observations[0].AssetManifestDigest != context.Assets.Digest ||
            observations[0].LimitsDigest != limits.Digest())
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1038 requires one exact target XML observation bound to the selected rules, assets and limits.");
            yield break;
        }
        var xml = observations[0];
        var regional = xml.Backbones.Where(item => item.Kind == BackboneKind.UsRegional).ToArray();
        var incomplete = regional.Length != 1 || xml.Checks.Any(check =>
            (check.RuleId is "XML-OFFLINE" or "XML-LIMITS") && check.Status != CheckStatus.Pass ||
            check.Findings.Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE"));
        var findings = new List<ValidationFinding>();
        var failed = false;
        var work = limits.MaxXmlNodes;
        var documentPaths = new HashSet<string>(StringComparer.Ordinal);
        var duplicatePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in regional)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!documentPaths.Add(document.LogicalPath)) duplicatePaths.Add(document.LogicalPath);
        }
        foreach (var document in regional)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!document.ReadComplete || document.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal) ||
                duplicatePaths.Contains(document.LogicalPath)) { incomplete = true; continue; }
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var unique = true;
            var roots = 0;
            ParsedXmlElement? root = null;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                if (!elements.TryAdd(element.NodePath, element)) unique = false;
                if (element.ParentPath is null) { roots++; root = element; }
            }
            if (!unique || roots != 1 || root is not { LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" })
            { incomplete = true; continue; }
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { incomplete = true; break; }
                if (leaf.Location.LogicalPath != document.LogicalPath || leaf.Location.NodePath != leaf.NodePath)
                { incomplete = true; continue; }
                var scope = IsFormOrCover(leaf, elements, limits.MaxXmlDepth, ref work, cancellationToken);
                if (scope is null) { incomplete = true; continue; }
                if (scope == false) continue;
                var operation = elements[leaf.NodePath].Attributes.GetValueOrDefault("operation");
                if (operation is not { LocalName: "operation", NamespaceUri: "", IsDefault: false, Value: "new" or "replace" or "delete" or "append" }) { incomplete = true; continue; }
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

    private static bool? IsFormOrCover(ParsedPackageLeaf leaf, Dictionary<string, ParsedXmlElement> elements,
        int maximumDepth, ref int work, CancellationToken token)
    {
        if (!elements.TryGetValue(leaf.NodePath, out var element) || element.LocalName != "leaf" || element.NamespaceUri.Length != 0) return null;
        var parentPath = element.ParentPath;
        var seen = new HashSet<string>(StringComparer.Ordinal) { leaf.NodePath };
        var chain = new List<string>();
        var leadingExtensions = true;
        var depth = 0;
        while (parentPath is not null)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || ++depth > maximumDepth || !seen.Add(parentPath) || !elements.TryGetValue(parentPath, out var current)) return null;
            if (current.ParentPath is null)
            {
                if (current.LocalName != "fda-regional" || current.NamespaceUri != "http://www.ich.org/fda") return null;
                return chain is ["m1-2-cover-letters", "m1-regional"] or ["form", "m1-1-forms", "m1-regional"] or
                    ["form", "submission-information", "application", "application-set", "admin"];
            }
            if (current.NamespaceUri.Length != 0) return null;
            if (!(leadingExtensions && current.LocalName == "node-extension"))
            { leadingExtensions = false; chain.Add(current.LocalName); }
            parentPath = current.ParentPath;
        }
        return null;
    }
}
