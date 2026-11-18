using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaUnreferencedFileRule
{
    internal const string RuleId = "FDA47-1306";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1306" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1306 requires the archived criteria and a selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2022, 3, 1))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection date precedes the effective date of criterion 1306.");
            yield break;
        }
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.IchQa))
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The selected standards snapshot requires ICH eCTD Q&A 36 item 13 for the module folder scope.");
            yield break;
        }
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        if (xml is null || xml.Backbones.Count == 0 || xml.Backbones.Any(document => !document.ReadComplete) ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE"))
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "A complete target backbone inventory is required to establish that a leaf is absent.");
            yield break;
        }
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in xml.Backbones)
        foreach (var leaf in document.Leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (leaf.Operation == "delete" || string.IsNullOrWhiteSpace(leaf.Href)) continue;
            try
            {
                var reference = PackageLogicalPath.ResolveReference(document.LogicalPath, leaf.Href);
                if (reference.LogicalPath is { } path) referenced.Add(path);
            }
            catch (ArgumentException) { /* Malformed hrefs are located by FILE-HREF. */ }
        }
        var findings = new List<ValidationFinding>();
        var omitted = false;
        foreach (var file in target.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (referenced.Contains(file.LogicalPath) || !IsModuleContent(file.LogicalPath)) continue;
            if (remaining <= 0) { omitted = true; break; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The target module file has no corresponding backbone leaf reference.",
                new(target.Manifest.SequenceNumber, file.LogicalPath, Files: [new(target.SourceId, file.LogicalPath)]), Actual: file.LogicalPath,
                RecommendedAction: "Review the backbone leaf and the exact path of the submitted file."));
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : omitted ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            omitted ? "Additional unreferenced files exceed the shared finding budget." : "Compared target module files with captured backbone leaf references.", findings);
    }

    private static bool IsModuleContent(string path)
    {
        var segments = path.Split('/');
        return segments.Length >= 3 && segments[1] is ("m1" or "m2" or "m3" or "m4" or "m5") &&
            Array.IndexOf(segments, "util", 2, segments.Length - 3) < 0;
    }
}
