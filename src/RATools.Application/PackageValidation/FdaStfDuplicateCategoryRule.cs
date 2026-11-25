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
        var limits = target.Limits;
        var xml = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || xml.Length != 1 ||
            xml[0].RulesDigest != context.Catalog.Digest || xml[0].AssetManifestDigest != context.Assets.Digest || xml[0].LimitsDigest != limits.Digest())
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1362 requires one exact current XML observation bound to rules, assets and limits.");
            yield break;
        }
        var stfs = xml[0].StudyTaggingFiles;
        var incomplete = !stfs.InventoryComplete;
        var findings = new List<ValidationFinding>();
        var work = limits.MaxXmlNodes;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var duplicatePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in stfs.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!paths.Add(document.LogicalPath)) duplicatePaths.Add(document.LogicalPath);
        }
        foreach (var document in stfs.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass || duplicatePaths.Contains(document.LogicalPath) ||
                !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal))
            { incomplete = true; continue; }
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var children = new Dictionary<string, List<ParsedXmlElement>>(StringComparer.Ordinal);
            var unique = true; var roots = 0; ParsedXmlElement? root = null;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                if (!nodes.Add(element.NodePath) || element.Location.LogicalPath != document.LogicalPath || element.Location.NodePath != element.NodePath) unique = false;
                if (element.ParentPath is not { } parent) { roots++; root = element; }
                else
                {
                    if (!children.TryGetValue(parent, out var siblings)) children.Add(parent, siblings = []);
                    siblings.Add(element);
                }
            }
            if (!unique || roots != 1 || root is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
            { incomplete = true; continue; }
            ParsedXmlElement? identifier = null; var identifiers = 0;
            foreach (var element in children.GetValueOrDefault(root.NodePath) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                if (element is { LocalName: "study-identifier", NamespaceUri.Length: 0 }) { identifier = element; identifiers++; }
            }
            if (!unique || identifiers != 1 || identifier is null) { incomplete = true; continue; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in children.GetValueOrDefault(identifier.NodePath) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { incomplete = true; break; }
                if (category is not { LocalName: "category", NamespaceUri.Length: 0 }) continue;
                if (work < 2) { work = 0; incomplete = true; break; }
                work -= 2;
                var attribute = category.Attributes.GetValueOrDefault("name");
                var infoType = category.Attributes.GetValueOrDefault("info-type");
                if (attribute is not { LocalName: "name", NamespaceUri: "" } || infoType is not { LocalName: "info-type", NamespaceUri: "" })
                { incomplete = true; continue; }
                var name = attribute.Value;
                var realm = name switch
                {
                    "species" or "route-of-admin" or "type-of-control" => "ich",
                    "duration" => "us",
                    _ => null
                };
                if (realm is null || infoType.Value != realm) { incomplete = true; continue; }
                if (seen.Add(name)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
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
