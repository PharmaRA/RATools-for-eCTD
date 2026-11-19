using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRegionalDtdVersionRule
{
    internal const string RuleId = "FDA47-1445";

    internal static RuleCheckResult? Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The regional version declaration inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1445" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1445 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1445.");

        var digest = target.InputDigest;
        cancellationToken.ThrowIfCancellationRequested();
        var inspections = lifecycle.XmlInspections.Where(xml => xml.InputDigest == digest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (inspections.Length != 1) return Unknown("A unique target XML inspection is required.");
        var xml = inspections[0];
        var sequencePrefix = target.Manifest.SequenceNumber + "/";
        var indexes = xml.Backbones.Where(document => document.Kind == BackboneKind.Ich && document.LogicalPath == sequencePrefix + "index.xml").ToArray();
        var incomplete = indexes.Length != 1 || !indexes[0].ReadComplete ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code is "INVALID_REGIONAL_BACKBONE_REFERENCE" or "REGIONAL_PROFILE_REQUIRED");
        var groups = xml.Backbones.Where(document => document.Kind == BackboneKind.UsRegional).GroupBy(document => document.LogicalPath, StringComparer.Ordinal).ToArray();
        if (groups.Length == 0) incomplete = true;
        var findings = new List<ValidationFinding>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() != 1 || !group.Key.StartsWith(sequencePrefix, StringComparison.Ordinal)) { incomplete = true; continue; }
            var document = group.Single();
            if (document.DocumentTypeName != "fda-regional:fda-regional") { incomplete = true; continue; }
            if (!document.ReadComplete) incomplete = true;
            var roots = document.Elements.Where(element => element.ParentPath is null).ToArray();
            if (roots.Length != 1 || roots[0].Name != "fda-regional:fda-regional" || roots[0].LocalName != "fda-regional" || roots[0].NamespaceUri != "http://www.ich.org/fda")
            { incomplete = true; continue; }
            var root = roots[0];
            var attribute = root.Attributes.GetValueOrDefault("dtd-version");
            if (attribute is not null && (attribute.LocalName != "dtd-version" || attribute.NamespaceUri.Length != 0)) { incomplete = true; continue; }
            if (attribute is { IsDefault: false })
            {
                if (attribute.Value != "3.3") incomplete = true;
                continue;
            }
            if (remaining <= 0) { incomplete = true; continue; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "A DTD default does not replace an explicit dtd-version declaration in a selected US regional backbone.",
                root.Location with { FieldPath = "@dtd-version", Files = [new(target.SourceId, document.LogicalPath)] },
                Expected: "Explicit dtd-version", Actual: attribute is null ? "Absent" : "DTD default: " + attribute.Value,
                RecommendedAction: "Review the explicit DTD version declaration in the selected US regional XML."));
            retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A regional version omission is established; remaining roots may be incomplete.", findings.ToArray()));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked explicit DTD versions on the target selected US regional roots. Unavailable roots or incomplete selection prevent a complete pass.", findings);
    }

    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
