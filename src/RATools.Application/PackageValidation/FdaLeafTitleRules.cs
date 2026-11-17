using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaLeafTitleRules
{
    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        foreach (var rule in context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).Where(rule => rule.InternalRuleId is "FDA47-1276" or "FDA47-1289"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authorized = rule.AuthorityRuleId == rule.InternalRuleId[6..] && rule.RuleVersion == "4.7.1" &&
                context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
                source.Kind == RuleSourceKind.RegulatorySpecification && source.Version == "4.7" && source.VerificationStatus == "Verified" &&
                source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" && context.HasArchivedOfficialSource(source);
            if (!authorized || context.SelectionDate is null)
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotEvaluated, "Leaf title checks require the exact archived official source and selection date.");
                continue;
            }
            if (context.SelectionDate < new DateOnly(2008, 3, 10))
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotApplicable, "The selected date precedes the official effective date.");
                continue;
            }
            var findings = new List<ValidationFinding>();
            var incomplete = xml is null || xml.Backbones.Count == 0 ||
                !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
                !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass);
            foreach (var document in xml?.Backbones ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!document.ReadComplete) { incomplete = true; continue; }
                var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
                foreach (var leaf in document.Leaves)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var titles = elements[leaf.NodePath].Children.Select(path => elements[path])
                        .Where(item => item.LocalName == "title" && item.NamespaceUri.Length == 0).ToArray();
                    if (titles.Length > 1 || titles.Any(item => item.Children.Count > 0)) { incomplete = true; continue; }
                    if (rule.AuthorityRuleId == "1276" && titles.Length == 0) { incomplete = true; continue; }
                    var text = titles.SingleOrDefault()?.Text;
                    var invalid = rule.AuthorityRuleId == "1289" ? string.IsNullOrWhiteSpace(text) : text!.StartsWith(' ') || text.EndsWith(' ');
                    if (!invalid) continue;
                    if (remaining == 0) { incomplete = true; continue; }
                    remaining--;
                    var location = (titles.SingleOrDefault()?.Location ?? leaf.Location) with
                    { FieldPath = "title", LeafId = leaf.Id, Files = [new(target.SourceId, document.LogicalPath)] };
                    findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                        rule.AuthorityRuleId == "1276" ? "The leaf title begins or ends with a space." : "The leaf has no nonblank title.", location,
                        Actual: text, RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; full regional qualification remains pending."));
                }
            }
            yield return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
                incomplete ? "Some leaf titles could not be evaluated because parsing, title structure or the shared finding budget was incomplete." :
                    "Checked original target-sequence leaf/title elements without trimming text; history retains its own rule scope.", findings);
        }
    }
}
