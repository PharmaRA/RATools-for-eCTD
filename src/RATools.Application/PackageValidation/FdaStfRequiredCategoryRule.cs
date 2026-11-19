using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfRequiredCategoryRule
{
    private static readonly string[] ToxicityCategories = ["species", "route-of-admin"];
    private static readonly string[] SpeciesCategory = ["species"];
    private static readonly string[] ControlCategory = ["type-of-control"];

    internal static RuleCheckResult Inspect(ICapturedPackageInput target, PackageXmlInspection? xml,
        PackageStandardsContext context, ValidationRuleDescriptor rule, int remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (xml is null) return Unknown("Target XML observations are unavailable.");
        var stfs = xml.StudyTaggingFiles;
        if (stfs.Documents.Count == 0) return stfs.InventoryComplete
            ? new(rule.InternalRuleId, CheckStatus.Pass, "No target STF requires category assessment.")
            : Unknown("STF discovery did not establish a complete inventory.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) ||
            !context.Assets.Assets.TryGetValue("stf-dtd", out var asset) ||
            asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f")
            return Unknown("STF category assessment requires the archived specification and pinned STF DTD.");
        var incomplete = !stfs.InventoryComplete;
        var requirements = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var duration = new HashSet<string>(StringComparer.Ordinal);
        var paths = stfs.Documents.Select(document => document.LogicalPath).ToHashSet(StringComparer.Ordinal);
        foreach (var index in xml.Backbones.Where(document => document.Kind == BackboneKind.Ich))
        {
            token.ThrowIfCancellationRequested();
            if (!index.ReadComplete || index.DtdStatus != CheckStatus.Pass) { incomplete = true; continue; }
            var elements = index.Elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
            foreach (var leaf in index.Leaves)
            {
                token.ThrowIfCancellationRequested();
                if (leaf.Operation == "delete" || leaf.IsRegionalReference) continue;
                if (leaf.Operation is not ("new" or "replace" or "append") || leaf.Href is null) { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || !paths.Contains(path)) continue;
                if (reference.Query is not null || reference.Fragment is not null) { incomplete = true; continue; }
                var heading = QualifiedHeading(leaf, elements, context, token);
                if (heading is null) { incomplete = true; continue; }
                if (!requirements.TryGetValue(path, out var required)) requirements.Add(path, required = new(StringComparer.Ordinal));
                var categories = heading switch
                {
                    "m4-2-3-1-single-dose-toxicity" or "m4-2-3-2-repeat-dose-toxicity" => ToxicityCategories,
                    "m4-2-3-4-1-long-term-studies" => SpeciesCategory,
                    "m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication" => ControlCategory,
                    _ => []
                };
                required.UnionWith(categories);
                if (heading == "m4-2-3-2-repeat-dose-toxicity") duration.Add(path);
            }
        }
        var findings = new List<ValidationFinding>();
        foreach (var document in stfs.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass || !requirements.TryGetValue(document.LogicalPath, out var required))
            { incomplete = true; continue; }
            var root = document.Elements.SingleOrDefault(element => element.ParentPath is null);
            if (root is not { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" }) { incomplete = true; continue; }
            var identifiers = document.Elements.Where(element => element.ParentPath == root.NodePath && Plain(element, "study-identifier")).ToArray();
            if (identifiers.Length != 1) { incomplete = true; continue; }
            var identifier = identifiers[0];
            var categories = document.Elements.Where(element => element.ParentPath == identifier.NodePath && Plain(element, "category"))
                .ToLookup(element => element.Attribute("name", "") ?? "", StringComparer.Ordinal);
            foreach (var name in required)
            {
                token.ThrowIfCancellationRequested();
                var matches = categories[name].ToArray();
                // Other realms and duplicate declarations need their own qualification/rule.
                if (matches.Length > 1 || matches.Any(element => element.Attribute("info-type", "") != "ich"))
                { incomplete = true; continue; }
                if (matches.Length == 1 && !string.IsNullOrWhiteSpace(matches[0].Text)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                var observed = matches.SingleOrDefault();
                findings.Add(new(rule.InternalRuleId, CheckStatus.Fail, rule.Severity, rule.InternalRuleId,
                    "The STF omits a nonblank category required by its referenced ICH section.",
                    (observed?.Location ?? identifier.Location) with
                    { FieldPath = "category[@name='" + name + "'][@info-type='ich']", Files = [new(target.SourceId, document.LogicalPath)] },
                    Expected: name, Actual: observed?.Text));
            }
            if (duration.Contains(document.LogicalPath))
            {
                var matches = categories["duration"].ToArray();
                if (matches.Length != 1 || matches[0].Attribute("info-type", "") != "us" || string.IsNullOrWhiteSpace(matches[0].Text))
                    incomplete = true; // "duration if applicable" cannot be inferred from a section or filename.
            }
        }
        return new(rule.InternalRuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked known STF categories against actual ICH section references; duration applicability and regional variants remain unqualified.", findings);

        RuleCheckResult Unknown(string reason) => new(rule.InternalRuleId, CheckStatus.NotEvaluated, reason);
    }

    private static bool Plain(ParsedXmlElement element, string name) => element.LocalName == name && element.NamespaceUri.Length == 0;

    internal static string? QualifiedHeading(ParsedPackageLeaf leaf, Dictionary<string, ParsedXmlElement> elements,
        PackageStandardsContext context, CancellationToken token)
    {
        if (!elements.TryGetValue(leaf.NodePath, out var element) || element.ParentPath is not { } parent || !elements.TryGetValue(parent, out var heading)) return null;
        while (Plain(heading, "node-extension"))
        {
            token.ThrowIfCancellationRequested();
            if (heading.ParentPath is not { } path || !elements.TryGetValue(path, out heading)) return null;
        }
        var name = heading.LocalName;
        if (heading.NamespaceUri.Length != 0 || !context.NodeSchema.Definitions.TryGetValue(name, out var definition)) return null;
        while (definition.ParentDefinitionKey is { } parentKey)
        {
            token.ThrowIfCancellationRequested();
            if (heading.ParentPath is not { } path || !elements.TryGetValue(path, out heading) || !Plain(heading, parentKey)) return null;
            definition = context.NodeSchema.Get(parentKey);
        }
        return heading.ParentPath is { } rootPath && elements.TryGetValue(rootPath, out var root) &&
            root is { ParentPath: null, LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } ? name : null;
    }
}
