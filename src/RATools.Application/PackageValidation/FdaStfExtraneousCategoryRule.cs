using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfExtraneousCategoryRule
{
    internal static RuleCheckResult Inspect(ICapturedPackageInput target, PackageXmlInspection? xml,
        PackageStandardsContext context, ValidationRuleDescriptor rule, int remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (xml is null) return Unknown("Target XML observations are unavailable.");
        var stfs = xml.StudyTaggingFiles;
        if (stfs.Documents.Count == 0) return stfs.InventoryComplete
            ? new(rule.InternalRuleId, CheckStatus.Pass, "No target STF categories require assessment.")
            : Unknown("STF discovery is incomplete.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) ||
            !context.Assets.Assets.TryGetValue("stf-dtd", out var asset) ||
            asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f")
            return Unknown("STF category assessment requires the archived specification and pinned DTD.");
        var indexes = xml.Backbones.Where(document => document.Kind == BackboneKind.Ich).ToArray();
        if (indexes.Length != 1 || !indexes[0].ReadComplete || indexes[0].DtdStatus != CheckStatus.Pass)
            return Unknown("A complete DTD-valid target ICH index is needed to establish all STF placements.");
        var index = indexes[0];
        var elements = index.Elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
        var paths = stfs.Documents.Select(document => document.LogicalPath).ToHashSet(StringComparer.Ordinal);
        var allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var uncertain = new HashSet<string>(StringComparer.Ordinal);
        var unresolvedReference = false;
        foreach (var leaf in index.Leaves)
        {
            token.ThrowIfCancellationRequested();
            if (leaf.IsRegionalReference || leaf.Operation == "delete") continue;
            if (string.IsNullOrWhiteSpace(leaf.Href)) { unresolvedReference = true; continue; }
            PackageReference reference;
            try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href); }
            catch (ArgumentException) { unresolvedReference = true; continue; }
            if (reference.LogicalPath is not { } path || !paths.Contains(path)) continue;
            if (reference.Query is not null || reference.Fragment is not null || leaf.Operation is not ("new" or "replace" or "append"))
            { uncertain.Add(path); continue; }
            var heading = FdaStfRequiredCategoryRule.QualifiedHeading(leaf, elements, context, token);
            if (heading is null) { uncertain.Add(path); continue; }
            if (!allowed.TryGetValue(path, out var names)) allowed.Add(path, names = new(StringComparer.Ordinal));
            switch (heading)
            {
                case "m4-2-3-1-single-dose-toxicity":
                    names.Add("species"); names.Add("route-of-admin"); break;
                case "m4-2-3-2-repeat-dose-toxicity":
                    names.Add("species"); names.Add("route-of-admin"); names.Add("duration"); break;
                case "m4-2-3-4-1-long-term-studies":
                    names.Add("species"); names.Add("duration"); break; // Explicit example in STF specification p. 7.
                case "m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication":
                    names.Add("type-of-control"); break;
            }
        }
        if (unresolvedReference) return Unknown("An unresolved target reference prevents a complete STF placement set.");
        var incomplete = !stfs.InventoryComplete;
        var findings = new List<ValidationFinding>();
        foreach (var document in stfs.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass || uncertain.Contains(document.LogicalPath) ||
                !allowed.TryGetValue(document.LogicalPath, out var names))
            { incomplete = true; continue; }
            var roots = document.Elements.Where(element => element.ParentPath is null).ToArray();
            if (roots.Length != 1 || roots[0] is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
            { incomplete = true; continue; }
            var identifiers = document.Elements.Where(element => element.ParentPath == roots[0].NodePath &&
                element is { LocalName: "study-identifier", NamespaceUri.Length: 0 }).ToArray();
            if (identifiers.Length != 1) { incomplete = true; continue; }
            foreach (var category in document.Elements.Where(element => element.ParentPath == identifiers[0].NodePath &&
                         element is { LocalName: "category", NamespaceUri.Length: 0 }))
            {
                token.ThrowIfCancellationRequested();
                var name = category.Attribute("name", "");
                var realm = category.Attribute("info-type", "");
                if (name == "duration" && realm == "us")
                {
                    if (!names.Contains(name)) incomplete = true; // No inferred duration applicability outside qualified examples.
                    continue;
                }
                if (name is not ("species" or "route-of-admin" or "type-of-control") || realm != "ich")
                { incomplete = true; continue; }
                if (names.Contains(name)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                var attribute = category.Attributes["name"];
                findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                    "This known STF category is not called for by any of the document's qualified ICH section references.",
                    category.Location with { FieldPath = "@name", Line = attribute.Line, Column = attribute.Column,
                        Files = [new(target.SourceId, document.LogicalPath)] }, Actual: name));
            }
        }
        return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked known STF categories against the union of actual qualified section references. Duration outside qualified examples and regional or unknown categories remain unevaluated.", findings);

        RuleCheckResult Unknown(string reason) => new(rule.InternalRuleId, CheckStatus.NotEvaluated, reason);
    }
}
