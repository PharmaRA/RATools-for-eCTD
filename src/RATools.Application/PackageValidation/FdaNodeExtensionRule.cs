using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaNodeExtensionRule
{
    internal const string RuleId = "FDA47-1476";

    internal static RuleCheckResult? Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The node-extension inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1476" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1476 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1476.");
        var digest = target.InputDigest;
        cancellationToken.ThrowIfCancellationRequested();
        var inspections = lifecycle.XmlInspections.Where(xml => xml.InputDigest == digest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (inspections.Length != 1) return Unknown("A unique target XML inspection is required.");
        var xml = inspections[0];
        var prefix = target.Manifest.SequenceNumber + "/";
        var incomplete = !xml.Backbones.Any(document => document.Kind == BackboneKind.Ich && document.LogicalPath == prefix + "index.xml") ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code is "INVALID_REGIONAL_BACKBONE_REFERENCE" or "REGIONAL_PROFILE_REQUIRED");
        var findings = new List<ValidationFinding>();
        foreach (var group in xml.Backbones.GroupBy(document => document.LogicalPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() != 1 || !group.Key.StartsWith(prefix, StringComparison.Ordinal)) { incomplete = true; continue; }
            var document = group.Single();
            if (document.Kind is not (BackboneKind.Ich or BackboneKind.UsRegional)) { incomplete = true; continue; }
            if (!document.ReadComplete) incomplete = true;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.Name != "node-extension" || element.LocalName != "node-extension" || element.NamespaceUri.Length != 0) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "A node-extension is present in the target submission backbone.",
                    element.Location with { Files = [new(target.SourceId, document.LogicalPath)] },
                    Expected: "No node-extension", Actual: "node-extension",
                    RecommendedAction: "Review the use of existing eCTD headings in future submissions; FDA strongly discourages node extensions."));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A node-extension is established; remaining documents may be incomplete.", findings.ToArray()));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked target ICH/US node-extension elements independently of their IDs and lifecycle identity. Incomplete XML selection cannot establish absence.", findings);
    }

    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
