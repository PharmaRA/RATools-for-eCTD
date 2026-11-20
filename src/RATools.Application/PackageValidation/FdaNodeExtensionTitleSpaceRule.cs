using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaNodeExtensionTitleSpaceRule
{
    internal const string RuleId = "FDA47-1482";

    internal static RuleCheckResult? Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The node-extension title space inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1482" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1482 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1482.");
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
            var elements = document.Elements.GroupBy(element => element.NodePath, StringComparer.Ordinal).ToArray();
            if (elements.Any(items => items.Count() != 1)) { incomplete = true; continue; }
            var byPath = elements.ToDictionary(items => items.Key, items => items.Single(), StringComparer.Ordinal);
            foreach (var extension in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (extension.Name != "node-extension" || extension.LocalName != "node-extension" || extension.NamespaceUri.Length != 0) continue;
                foreach (var path in extension.Children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!byPath.TryGetValue(path, out var title) || title.ParentPath != extension.NodePath) { incomplete = true; continue; }
                    if (title.Name != "title" || title.LocalName != "title" || title.NamespaceUri.Length != 0) continue;
                    if (title.Children.Count > 0) { incomplete = true; continue; }
                    // The first retained character cannot change when more text is read; the last can.
                    if (!title.Text.StartsWith(' ') && !(document.ReadComplete && title.Text.EndsWith(' '))) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The node-extension title begins or ends with U+0020 space.",
                        title.Location with { FieldPath = "title", Files = [new(target.SourceId, document.LogicalPath)] },
                        Expected: "No leading or trailing U+0020", Actual: title.Text,
                        RecommendedAction: "Review title boundary spaces and use existing eCTD headings in future submissions."));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A title boundary space is established; remaining text or scope may be incomplete.", findings.ToArray()));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked exact direct node-extension titles without trimming or decoding twice. Incomplete text cannot establish its final trailing character.", findings);
    }

    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
