using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaFileExtensionRule
{
    internal const string RuleId = "FDA47-1255";
    private static readonly ValidationRuleSource FormatSource = new("fda-file-formats", RuleSourceKind.RegulatorySpecification,
        "9.3", "https://www.fda.gov/media/85816/download?attachment", "8d5b4f2ef220c16324d83e44cc50595e087ccd44d1db42ebcca4084976abae6b", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1255" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.VerificationStatus != "Verified" || source.Version != "4.7" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The criteria source and selection date are required.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of criterion 1255.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2025, 8, 20) || !context.HasArchivedOfficialSource(FormatSource))
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "This check requires the archived file-format revision 9.3 and a selection on or after its revision date.");
            yield break;
        }
        var limits = target.Limits;
        var xmls = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || xmls.Length != 1 ||
            xmls[0].RulesDigest != context.Catalog.Digest || xmls[0].AssetManifestDigest != context.Assets.Digest || xmls[0].LimitsDigest != limits.Digest())
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Module assignment requires one exact XML observation bound to the capture, rules, assets and limits.");
            yield break;
        }
        var indexes = xmls[0].Backbones.Where(item => item.LogicalPath == target.Manifest.SequenceNumber + "/index.xml").Take(2).ToArray();
        var index = indexes.Length == 1 ? indexes[0] : null;
        if (index is null || index.Kind != BackboneKind.Ich || index.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
            !index.ReadComplete || index.DtdStatus != CheckStatus.Pass)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Module assignment requires a fully read, DTD-valid ICH backbone.");
            yield break;
        }
        var findings = new List<ValidationFinding>();
        var work = limits.MaxXmlNodes;
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in target.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0 || !files.Add(file.LogicalPath))
            {
                yield return new(RuleId, CheckStatus.NotEvaluated, "Captured file indexing is incomplete or ambiguous.");
                yield break;
            }
        }
        var profile = context.Assets.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.Kind == BackboneKind.Ich);
        var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
        var children = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string?>(StringComparer.Ordinal);
        ParsedXmlElement? root = null;
        var unique = true;
        foreach (var element in index.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { unique = false; break; }
            unique &= elements.TryAdd(element.NodePath, element);
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in element.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                unique &= members.Add(child);
                if (!owners.TryAdd(child, element.NodePath)) owners[child] = null;
            }
            children.TryAdd(element.NodePath, members);
            if (element.ParentPath is not null) continue;
            if (root is not null) unique = false;
            root = element;
        }
        var leafPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in index.Leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { unique = false; break; }
            unique &= leafPaths.Add(leaf.NodePath);
        }
        if (!unique || !files.Contains(index.LogicalPath) || profile is null || root is null ||
            root.LocalName != profile.RootLocalName || root.NamespaceUri != profile.NamespaceUri ||
            root.Location.LogicalPath != index.LogicalPath || root.Location.NodePath != root.NodePath)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Module assignment requires a captured ICH root and unique bounded element and leaf facts.");
            yield break;
        }
        var incomplete = false;
        foreach (var leaf in index.Leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (leaf.IsRegionalReference || leaf.Operation == "delete") continue;
            if (leaf.Operation is not ("new" or "replace" or "append") || string.IsNullOrWhiteSpace(leaf.Href) ||
                leaf.Location.LogicalPath != index.LogicalPath || leaf.Location.NodePath != leaf.NodePath || leaf.Location.LeafId != leaf.Id ||
                !elements.TryGetValue(leaf.NodePath, out var element) || element.LocalName != "leaf" || element.NamespaceUri.Length != 0 ||
                element.ParentPath != leaf.ParentNodePath ||
                !Explicit(element, "ID", "", leaf.Id, ref work, cancellationToken) ||
                !Explicit(element, "operation", "", leaf.Operation, ref work, cancellationToken) ||
                !Explicit(element, "href", "http://www.w3c.org/1999/xlink", leaf.Href, ref work, cancellationToken))
            { incomplete = true; continue; }
            var module = Module(element, root, elements, children, owners, index.LogicalPath, limits.MaxXmlDepth, ref work, cancellationToken);
            if (module == 0)
            { incomplete = true; continue; }
            PackageReference reference;
            try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href); }
            catch (ArgumentException) { incomplete = true; continue; }
            if (reference.LogicalPath is not { } path || reference.Query is not null || !files.Contains(path))
            { incomplete = true; continue; }
            bool? allowed = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".pdf" or ".jpg" or ".jpeg" or ".png" => true,
                ".txt" or ".xls" or ".xlsx" or ".xpt" or ".r" or ".sas" or ".svg" => module >= 3,
                ".rmd" or ".json" or ".cas" or ".dat" => module == 5,
                ".sdf" => module == 3,
                _ => null
            };
            if (allowed == true) continue;
            if (allowed is null || remaining <= 0) { incomplete = true; continue; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, $"The extension is outside the accepted eCTD modules in FDA format revision 9.3 (M{module}).",
                leaf.Location with { FieldPath = "@xlink:href", Files = [new(target.SourceId, index.LogicalPath), new(target.SourceId, path)] }, Actual: leaf.Href,
                RecommendedAction: "Review the referenced file and its placement in the eCTD backbone."));
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked selected format rows for target ICH module 2 through 5 content leaves. Regional and remaining file-format rows require separate assessment.", findings);
    }

    private static bool Explicit(ParsedXmlElement element, string name, string ns, string? expected, ref int work, CancellationToken token)
    {
        ParsedXmlAttributeValue? found = null;
        foreach (var attribute in element.Attributes.Values)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) return false;
            if (attribute.LocalName != name || attribute.NamespaceUri != ns) continue;
            if (found is not null) return false;
            found = attribute;
        }
        return found is { IsDefault: false, ExplicitValue: not null } && found.Value == expected && found.ExplicitValue == found.Value;
    }

    private static int Module(ParsedXmlElement element, ParsedXmlElement root, Dictionary<string, ParsedXmlElement> elements,
        Dictionary<string, HashSet<string>> children, Dictionary<string, string?> owners, string path, int maxDepth, ref int work, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0;
        var module = 0;
        while (element != root)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || ++depth > maxDepth || !seen.Add(element.NodePath) || element.NamespaceUri.Length != 0 ||
                element.Location.LogicalPath != path || element.Location.NodePath != element.NodePath ||
                element.ParentPath is not { } parent || !elements.TryGetValue(parent, out var owner) ||
                !owners.TryGetValue(element.NodePath, out var declaredOwner) || declaredOwner != parent ||
                !children.TryGetValue(parent, out var members) || !members.Contains(element.NodePath)) return 0;
            var current = element.LocalName switch
            {
                "m2-common-technical-document-summaries" => 2, "m3-quality" => 3,
                "m4-nonclinical-study-reports" => 4, "m5-clinical-study-reports" => 5, _ => 0
            };
            if (current != 0)
            {
                if (owner != root || module != 0) return 0;
                module = current;
            }
            element = owner;
        }
        return module;
    }
}
