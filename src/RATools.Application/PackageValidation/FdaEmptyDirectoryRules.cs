using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaEmptyDirectoryRules
{
    internal const string RuleId = "FDA47-1322";
    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, string profile,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(profile).SingleOrDefault(rule => rule.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = rule.AuthorityRuleId == "1322" && rule.RuleVersion == "4.7.1" &&
            context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
            source.Kind == RuleSourceKind.RegulatorySpecification && source.Version == "4.7" && source.VerificationStatus == "Verified" &&
            source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" && context.HasArchivedOfficialSource(source);
        if (!authorized || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1322 requires the exact archived official source and explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2013, 4, 30))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 1322's effective date.");
            yield break;
        }
        var findings = new List<ValidationFinding>();
        var limited = false;
        foreach (var directory in target.Manifest.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!directory.IsEmpty) continue;
            if (remaining == 0) { limited = true; break; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                "The captured target-sequence folder contains no files or subfolders.",
                new(target.Manifest.SequenceNumber, directory.LogicalPath, Files: []),
                RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; omit empty folders from future submissions."));
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : limited ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            limited ? "Additional empty directories exceed the shared file-stage finding budget and remain unevaluated." :
                "Checked target-sequence directory inventory; parent directories containing subfolders and surrounding inputs are not classified as empty.", findings);
    }
}
