using System.Text;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaMaterialIdLengthRule
{
    internal const string RuleId = "FDA47-1550";
    private static readonly string[] Ancestors = ["m1-15-2-materials", "m1-15-promotional-material", "m1-regional"];
    private static readonly ValidationRuleSource M1Specification = new("fda-m1-specification", RuleSourceKind.RegulatorySpecification, "2.6",
        "https://www.fda.gov/media/159382/download?attachment", "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2", "Verified");

    internal static RuleCheckResult? Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The material-id length inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1550" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1550 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2015, 6, 15))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1550.");
        if (!context.HasArchivedOfficialSource(M1Specification)) return Unknown("The captured M1 material-id specification is required.");
        var digest = target.InputDigest;
        cancellationToken.ThrowIfCancellationRequested();
        var inspections = lifecycle.XmlInspections.Where(xml => xml.InputDigest == digest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (inspections.Length != 1) return Unknown("A unique target XML inspection is required.");
        var xml = inspections[0];
        var prefix = target.Manifest.SequenceNumber + "/";
        var incomplete = !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code is "INVALID_REGIONAL_BACKBONE_REFERENCE" or "REGIONAL_PROFILE_REQUIRED");
        var documents = xml.Backbones.Where(document => document.Kind == BackboneKind.UsRegional).GroupBy(document => document.LogicalPath, StringComparer.Ordinal).ToArray();
        if (documents.Length == 0) incomplete = true;
        var findings = new List<ValidationFinding>();
        foreach (var group in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() != 1 || !group.Key.StartsWith(prefix, StringComparison.Ordinal)) { incomplete = true; continue; }
            var document = group.Single();
            if (document.DocumentTypeName != "fda-regional:fda-regional") { incomplete = true; continue; }
            if (!document.ReadComplete) incomplete = true;
            var nodeGroups = document.Elements.GroupBy(element => element.NodePath, StringComparer.Ordinal).ToArray();
            if (nodeGroups.Any(nodes => nodes.Count() != 1)) { incomplete = true; continue; }
            var byPath = nodeGroups.ToDictionary(nodes => nodes.Key, nodes => nodes.Single(), StringComparer.Ordinal);
            foreach (var material in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Is(material, "m1-15-2-1-material")) continue;
                var current = material;
                var qualified = true;
                foreach (var name in Ancestors)
                {
                    if (current.ParentPath is not { } path || !byPath.TryGetValue(path, out var parent))
                    { incomplete = true; qualified = false; break; }
                    if (!parent.Children.Contains(current.NodePath, StringComparer.Ordinal)) { incomplete = true; qualified = false; break; }
                    if (!Is(parent, name)) { qualified = false; break; }
                    current = parent;
                }
                if (!qualified) continue;
                if (current.ParentPath is not { } rootPath || !byPath.TryGetValue(rootPath, out var root)) { incomplete = true; continue; }
                if (root.ParentPath is not null || root.Name != "fda-regional:fda-regional" || root.NamespaceUri != "http://www.ich.org/fda" ||
                    !root.Children.Contains(current.NodePath, StringComparer.Ordinal)) { incomplete = true; continue; }
                var attribute = material.Attributes.GetValueOrDefault("material-id");
                if (attribute is null) continue; // Requiredness is a separate rule/DTD concern.
                if (attribute.IsDefault || attribute.LocalName != "material-id" || attribute.NamespaceUri.Length != 0) { incomplete = true; continue; }
                var count = 0;
                foreach (var rune in attribute.Value.EnumerateRunes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _ = rune;
                    if (++count > 30) break;
                }
                if (count <= 30) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The material-id exceeds 30 XML characters (Unicode scalars).",
                    material.Location with { FieldPath = "@material-id", Line = attribute.Line > 0 ? attribute.Line : material.Location.Line,
                        Column = attribute.Column > 0 ? attribute.Column : material.Location.Column, Files = [new(target.SourceId, document.LogicalPath)] },
                    Expected: "At most 30 Unicode scalars", Actual: attribute.Value,
                    RecommendedAction: "Keep each material-id within the 30-character limit in future submissions."));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An overlong material-id is established; remaining scope may be incomplete.", findings.ToArray()));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Counted XML-decoded material-id values using the project's Unicode scalar convention without trimming or normalization. Requiredness and character classes are separate.", findings);
    }

    private static bool Is(ParsedXmlElement element, string name) => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0;
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
