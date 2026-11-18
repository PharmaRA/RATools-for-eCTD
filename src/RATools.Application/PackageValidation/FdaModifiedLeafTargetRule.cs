using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaModifiedLeafTargetRule
{
    internal const string RuleId = "FDA47-1153";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        if (rule.AuthorityRuleId != "1153" || rule.RuleVersion != "4.7.1" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Id != "fda-validation-criteria-4.7" ||
            source.Kind != RuleSourceKind.RegulatorySpecification || source.Version != "4.7" || source.VerificationStatus != "Verified" ||
            source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" || !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1153 requires the archived official source and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection date precedes FDA 1153's effective date.");
            yield break;
        }
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var incomplete = xml is null || xml.Backbones.Count == 0 || FdaExtraneousLeafPathRules.HasSkippedRegional(target, xml, cancellationToken) ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass);
        var events = lifecycle.Events.Where(item => item.Source.Location.SequenceNumber == target.Manifest.SequenceNumber)
            .ToDictionary(item => (item.Source.Location.LogicalPath, item.Source.NodePath));
        var findings = new List<ValidationFinding>();
        foreach (var document in xml?.Backbones ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.ReadComplete) { incomplete = true; continue; }
            var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(leaf.ModifiedFile)) continue;
                if (!events.TryGetValue((document.LogicalPath, leaf.NodePath), out var entry) || entry.TargetLeafLookup == CheckStatus.NotEvaluated)
                {
                    incomplete = true;
                    continue;
                }
                if (entry.TargetLeafLookup == CheckStatus.Pass) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                var attribute = elements[leaf.NodePath].Attributes["modified-file"];
                var location = leaf.Location with { FieldPath = "@modified-file", Line = attribute.Line > 0 ? attribute.Line : leaf.Location.Line,
                    Column = attribute.Column > 0 ? attribute.Column : leaf.Location.Column, Files = [new(target.SourceId, document.LogicalPath)] };
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The selected sequence contains no leaf at the referenced XML path and ID.",
                    location, Actual: leaf.ModifiedFile, RecommendedAction: "Review the referenced backbone path and leaf ID against the selected sequence."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            incomplete ? "Some references remain unresolved within the selected sequences, XML coverage or finding budget." : "Checked supplied modified-file references against the selected sequence manifests and XML leaf IDs.", findings);
    }
}
