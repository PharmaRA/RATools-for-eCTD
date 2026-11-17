using System.Text;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaFileNameRules
{
    public static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, string profile,
        PackageStandardsContext context, CancellationToken cancellationToken)
    {
        var remaining = target.Limits.MaxXmlFindings - 1;
        foreach (var rule in context.Catalog.ForProfile(profile).Where(rule => rule.InternalRuleId is "FDA47-1221" or "FDA47-1298"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceValid = rule.AuthorityRuleId == rule.InternalRuleId[6..] && rule.RuleVersion == "4.7.1" &&
                context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
                source.Kind == RuleSourceKind.RegulatorySpecification && source.Version == "4.7" && source.VerificationStatus == "Verified" &&
                source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" &&
                context.HasArchivedOfficialSource(source);
            if (!sourceValid || context.SnapshotId is null || context.SelectionDate is null)
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotEvaluated, "Official filename checks require the exact archived source, supported rule and explicit selection date.");
                continue;
            }
            var effective = rule.AuthorityRuleId == "1221" ? new DateOnly(2008, 3, 10) : new DateOnly(2013, 4, 30);
            if (context.SelectionDate < effective)
            {
                yield return new(rule.InternalRuleId, CheckStatus.NotApplicable, "The selected date precedes the official effective date.");
                continue;
            }
            var findings = new List<ValidationFinding>();
            var limited = false;
            foreach (var file in target.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = file.LogicalPath.Split('/')[^1];
                var dot = name.LastIndexOf('.');
                var invalid = rule.AuthorityRuleId == "1221" ? name.EnumerateRunes().Count() > 64 : dot <= 0 || dot == name.Length - 1;
                if (!invalid) continue;
                if (remaining == 0) { limited = true; break; }
                remaining--;
                findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                    rule.AuthorityRuleId == "1221" ? "The delivered filename exceeds 64 characters, including its extension." : "The delivered file has no filename extension.",
                    new(target.Manifest.SequenceNumber, file.LogicalPath, Files: [new(target.SourceId, file.LogicalPath)]), Actual: name,
                    RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; full regional qualification remains pending."));
            }
            yield return new(rule.InternalRuleId, findings.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                limited ? CheckStatus.NotEvaluated : CheckStatus.Pass,
                limited ? "Additional filename observations exceed the shared file-stage finding limit; remaining filenames were not evaluated." :
                    "Checked captured target-sequence filenames against the pinned FDA criterion; selected history retains its own rule scope.", findings);
        }
    }
}
