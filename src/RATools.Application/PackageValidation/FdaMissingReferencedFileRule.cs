using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaMissingReferencedFileRule
{
    internal const string RuleId = "FDA47-1323";

    internal static IEnumerable<RuleCheckResult> Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1323" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1323 requires the archived criteria and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2022, 3, 1))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1323.");
            yield break;
        }
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.IchQa))
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The selected snapshot requires ICH Q&A 36 item 12 for referenced file existence.");
            yield break;
        }
        var target = packages.Target;
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var incomplete = xml is null || xml.Backbones.Count == 0 ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        var files = packages.Sequences.Values.SelectMany(capture => capture.Manifest.Files)
            .Select(file => file.LogicalPath).ToHashSet(StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();
        foreach (var document in xml?.Backbones ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.ReadComplete) { incomplete = true; continue; }
            var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var element = elements[leaf.NodePath];
                var operation = element.Attributes.GetValueOrDefault("operation");
                if (operation is not { IsDefault: false, Value: "new" or "append" or "replace" or "delete" })
                { incomplete = true; continue; }
                if (operation.Value == "delete") continue;
                var attribute = element.Attributes.Values.FirstOrDefault(item => item.LocalName == "href" && item.NamespaceUri == "http://www.w3c.org/1999/xlink");
                if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value)) { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(document.LogicalPath, attribute.Value); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || reference.Query is not null)
                { incomplete = true; continue; }
                var sequence = path.Split('/')[0];
                if (string.CompareOrdinal(sequence, target.Manifest.SequenceNumber) > 0 || !packages.Sequences.ContainsKey(sequence))
                { incomplete = true; continue; }
                if (files.Contains(path)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The target backbone leaf references a file absent from the explicitly captured sequence.",
                    leaf.Location with { FieldPath = "@" + attribute.Name, Line = attribute.Line, Column = attribute.Column,
                        Files = [new(target.SourceId, document.LogicalPath)] },
                    Expected: "Delivered file at " + path, Actual: attribute.Value,
                    RecommendedAction: "Review the exact leaf reference and the files supplied in the selected sequence."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Compared target backbone hrefs with exact captured file paths. Unavailable history, unresolved paths and incomplete XML remain unevaluated; cross-sequence permission is assessed separately.", findings);
    }
}
