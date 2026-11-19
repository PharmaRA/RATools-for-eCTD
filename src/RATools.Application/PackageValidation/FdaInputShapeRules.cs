using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public static class FdaInputShapeRules
{
    public static IReadOnlyList<RuleCheckResult> Inspect(PackageInputShapeEvidence evidence, string profile,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var checks = new List<RuleCheckResult>();
        foreach (var rule in context.Catalog.ForProfile(profile).Where(rule => rule.InternalRuleId is "FDA47-3" or "FDA47-4")
            .OrderBy(rule => rule.InternalRuleId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rule.AuthorityRuleId != rule.InternalRuleId[6..] || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            { checks.Add(new(rule.InternalRuleId, CheckStatus.NotEvaluated, "Input shape rules require the archived FDA criteria and selection date.")); continue; }
            if (context.SelectionDate < new DateOnly(2016, 5, 31))
            { checks.Add(new(rule.InternalRuleId, CheckStatus.NotApplicable, "The selection precedes the rule's effective date.")); continue; }
            if (!Enum.IsDefined(evidence.Role) || evidence.Role == PackageInputRole.SingleFile && evidence.Files.Count != 1)
            { checks.Add(new(rule.InternalRuleId, CheckStatus.NotEvaluated, "Input role and completed inventory do not establish a valid shape observation.")); continue; }
            var failed = rule.InternalRuleId == "FDA47-3" ? evidence.Role == PackageInputRole.SingleFile : evidence.Files.Count == 0;
            if (!failed)
            { checks.Add(new(rule.InternalRuleId, CheckStatus.Pass, "Completed, stability-verified input shape does not violate this criterion; eCTD structure is assessed separately.")); continue; }
            if (remaining <= 0)
            { checks.Add(new(rule.InternalRuleId, CheckStatus.NotEvaluated, "The finding budget is exhausted.")); continue; }
            remaining--;
            checks.Add(new(rule.InternalRuleId, CheckStatus.Fail, "The explicitly selected submission shape violates the criterion.",
                [new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                    rule.InternalRuleId == "FDA47-3" ? "The input was supplied as a single file rather than a submission container." : "The completely scanned submission container contains no files.",
                    new(Files: []), Actual: rule.InternalRuleId == "FDA47-3" ? "SingleFile" : "0 files",
                    RecommendedAction: "Review the supplied submission folder and its files.")]));
        }
        return checks.AsReadOnly();
    }
}
