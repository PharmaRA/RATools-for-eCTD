using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRequiredUtilityRules
{
    internal const string PresenceRuleId = "FDA47-1119";
    internal const string ChecksumRuleId = "FDA47-1130";
    private const string MainDtdPath = "util/dtd/ich-ectd-3-2.dtd";
    private const string PublishedMd5 = "1d6f631cc6b6357f0f4fe378e5f79a27";
    private static readonly ValidationRuleSource IchSpecification = new("ich-specification", RuleSourceKind.RegulatorySpecification, "3.2.2",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf",
        "d8273833eac401ff3c094d782863b078110e33a7f358438d3f7a0dad277fc2b0", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle, PackageStandardsContext context,
        int remaining, CancellationToken cancellationToken)
    {
        var expectedPath = target.Manifest.SequenceNumber + "/" + MainDtdPath;
        var file = target.Manifest.Files.SingleOrDefault(item => item.LogicalPath == expectedPath);
        foreach (var rule in context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).Where(item => item.InternalRuleId is PresenceRuleId or ChecksumRuleId))
        {
            var check = Evaluate(rule, target, file, expectedPath, context, remaining, cancellationToken);
            remaining -= check.Findings.Count;
            if (check.Status == CheckStatus.NotApplicable) { yield return check; continue; }
            var conditional = FdaStfUtilityRules.Inspect(rule, target, lifecycle, context, remaining, cancellationToken);
            remaining -= conditional.Findings.Count;
            var status = check.Status == CheckStatus.Fail || conditional.Status == CheckStatus.Fail ? CheckStatus.Fail :
                check.Status == CheckStatus.NotEvaluated || conditional.Status == CheckStatus.NotEvaluated ? CheckStatus.NotEvaluated : CheckStatus.Pass;
            yield return new(rule.InternalRuleId, status, check.Reason + " " + conditional.Reason, check.Findings.Concat(conditional.Findings));
        }
    }

    private static RuleCheckResult Evaluate(ValidationRuleDescriptor rule, ICapturedPackageInput target, PackageInputFile? file,
        string expectedPath, PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = rule.InternalRuleId;
        var checksum = id == ChecksumRuleId;
        if (rule.AuthorityRuleId != (checksum ? "1130" : "1119") || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return new(id, CheckStatus.NotEvaluated, "Checking the required DTD needs the archived FDA criteria and a selection date.");
        if (context.SelectionDate < (checksum ? new DateOnly(2013, 4, 30) : new DateOnly(2008, 3, 10)))
            return new(id, CheckStatus.NotApplicable, "The selected date precedes the rule's effective date.");
        var sourceEvidence = checksum ? BundledOfficialSources.IchDtdChecksum : IchSpecification;
        if (!context.HasArchivedOfficialSource(sourceEvidence) || !context.HasArchivedOfficialSource(BundledOfficialSources.IchQa))
            return new(id, CheckStatus.NotEvaluated, "The selected snapshot requires the applicable ICH source and Q&A 36.");
        if (checksum && file is null)
            return new(id, CheckStatus.NotEvaluated, "The DTD is absent from the target sequence. Its checksum cannot be assessed.");
        if (checksum && file!.Md5 == PublishedMd5)
            return new(id, CheckStatus.Pass, "The captured ICH DTD matches the published MD5 checksum.");
        if (!checksum && file is not null)
            return new(id, CheckStatus.Pass, "The target sequence contains its required ICH backbone DTD.");
        if (remaining <= 0)
            return new(id, CheckStatus.NotEvaluated, "The shared finding budget is exhausted.");
        if (checksum)
            return new(id, CheckStatus.Fail, "The target ICH DTD differs from the published checksum.",
                [new(id, CheckStatus.Fail, rule.Severity, id, "The delivered ICH backbone DTD has a different MD5 checksum.",
                    new(target.Manifest.SequenceNumber, expectedPath, Files: [new(target.SourceId, expectedPath)]), Expected: PublishedMd5, Actual: file!.Md5,
                    RecommendedAction: "Review the delivered DTD against the ICH publication.")]);
        return new(id, CheckStatus.Fail, "The target sequence is missing the required ICH backbone DTD.",
            [new(id, CheckStatus.Fail, rule.Severity, id, "The required ICH DTD is absent at its delivery path.",
                new(target.Manifest.SequenceNumber, expectedPath, Files: []), Expected: MainDtdPath,
                RecommendedAction: "Include the required ICH backbone DTD in the sequence's util/dtd directory.")]);
    }
}
