using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfDuplicateCategoryRule
{
    internal const string RuleId = "FDA47-1362";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1362" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1362 requires its archived criteria and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1362.");
            yield break;
        }
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) ||
            !context.Assets.Assets.TryGetValue("stf-dtd", out var asset) ||
            asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f")
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "STF duplicate-category assessment requires the archived STF specification and pinned DTD.");
            yield break;
        }
        var xml = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (xml.Length != 1)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Target XML facts are missing or ambiguous.");
            yield break;
        }
        var stfs = xml[0].StudyTaggingFiles;
        var incomplete = !stfs.InventoryComplete;
        var findings = new List<ValidationFinding>();
        foreach (var document in stfs.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass) { incomplete = true; continue; }
            var roots = document.Elements.Where(element => element.ParentPath is null).ToArray();
            if (roots.Length != 1 || roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
            { incomplete = true; continue; }
            var identifiers = document.Elements.Where(element => element.ParentPath == roots[0].NodePath &&
                element is { LocalName: "study-identifier", NamespaceUri.Length: 0 }).ToArray();
            if (identifiers.Length != 1) { incomplete = true; continue; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in document.Elements.Where(element => element.ParentPath == identifiers[0].NodePath &&
                         element is { LocalName: "category", NamespaceUri.Length: 0 }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = category.Attribute("name", "");
                var realm = name switch
                {
                    "species" or "route-of-admin" or "type-of-control" => "ich",
                    "duration" => "us",
                    _ => null
                };
                if (realm is null || category.Attribute("info-type", "") != realm) { incomplete = true; continue; }
                if (seen.Add(name!)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                var attribute = category.Attributes["name"];
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "A known STF study category is declared more than once in this document.",
                    category.Location with { FieldPath = "@name", Line = attribute.Line, Column = attribute.Column,
                        Files = [new(target.SourceId, document.LogicalPath)] },
                    Expected: "One " + name + " category for realm " + realm, Actual: name));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked duplicate known study-identifier categories within each target STF. Unknown realms/categories and other business-attribute scopes remain unqualified; file-tags and properties are not treated as study categories.", findings);
    }
}
