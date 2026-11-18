using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRequiredUtilityRules
{
    internal const string PresenceRuleId = "FDA47-1119";
    private const string MainDtdPath = "util/dtd/ich-ectd-3-2.dtd";
    private static readonly ValidationRuleSource IchSpecification = new("ich-specification", RuleSourceKind.RegulatorySpecification, "3.2.2",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf",
        "d8273833eac401ff3c094d782863b078110e33a7f358438d3f7a0dad277fc2b0", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, string profile, PackageStandardsContext context,
        int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(profile).SingleOrDefault(item => item.InternalRuleId == PresenceRuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1119" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(PresenceRuleId, CheckStatus.NotEvaluated, "FDA 1119 requires the archived criteria and a selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(PresenceRuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 1119's effective date.");
            yield break;
        }
        if (!context.HasArchivedOfficialSource(IchSpecification) || !context.HasArchivedOfficialSource(BundledOfficialSources.IchQa))
        {
            yield return new(PresenceRuleId, CheckStatus.NotEvaluated, "The selected snapshot requires the archived ICH specification and Q&A 36.");
            yield break;
        }
        var expected = target.Manifest.SequenceNumber + "/" + MainDtdPath;
        if (target.Manifest.Files.Any(file => file.LogicalPath == expected))
            yield return new(PresenceRuleId, CheckStatus.Pass, "The target sequence contains its required ICH backbone DTD.");
        else if (remaining <= 0)
            yield return new(PresenceRuleId, CheckStatus.NotEvaluated, "The shared finding budget is exhausted.");
        else
            yield return new(PresenceRuleId, CheckStatus.Fail, "The target sequence is missing the required ICH backbone DTD.",
                [new(PresenceRuleId, CheckStatus.Fail, rule.Severity, PresenceRuleId, "The required ICH DTD is absent at its delivery path.",
                    new(target.Manifest.SequenceNumber, expected, Files: []), Expected: MainDtdPath,
                    RecommendedAction: "Include the required ICH backbone DTD in the sequence's util/dtd directory.")]);
    }
}
