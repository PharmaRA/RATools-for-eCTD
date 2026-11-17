using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRegionalFilePresence
{
    internal const string RuleId = "FDA47-2";
    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, string profile,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(profile).SingleOrDefault(rule => rule.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = rule.AuthorityRuleId == "2" && rule.RuleVersion == "4.7.1" &&
            context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
            source.Kind == RuleSourceKind.RegulatorySpecification && source.Version == "4.7" && source.VerificationStatus == "Verified" &&
            source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" && context.HasArchivedOfficialSource(source);
        if (!authorized || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 2 requires the exact archived official source and explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2017, 4, 2))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 2's effective date.");
            yield break;
        }
        var path = target.Manifest.SequenceNumber + "/m1/us/us-regional.xml";
        if (target.Manifest.Files.Any(file => file.LogicalPath == path))
        {
            yield return new(RuleId, CheckStatus.Pass, "The exact US regional backbone file exists in the captured target sequence; XML validity is assessed separately.");
            yield break;
        }
        if (remaining <= 0)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The shared file-stage finding budget is exhausted; missing regional-file evidence remains unevaluated.");
            yield break;
        }
        yield return new(RuleId, CheckStatus.Fail, "The captured target sequence is missing its required US regional backbone file.",
            [new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The required m1/us/us-regional.xml file is absent from the target sequence.",
                new(target.Manifest.SequenceNumber, path, Files: []),
                RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; include the target sequence's US regional backbone.")]);
    }
}
