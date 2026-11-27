using System.Globalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaTextLengthRule
{
    internal const string RuleId = "FDA47-1500";
    private static readonly ValidationRuleSource IchSpecification = new("ich-specification", RuleSourceKind.RegulatorySpecification, "3.2.2",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf",
        "d8273833eac401ff3c094d782863b078110e33a7f358438d3f7a0dad277fc2b0", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        if (rule.AuthorityRuleId != "1500" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || !context.HasArchivedOfficialSource(IchSpecification) || context.SelectionDate is null)
        { yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1500 requires the archived criteria, ICH specification and an explicit selection date."); yield break; }
        if (context.SelectionDate < new DateOnly(2013, 4, 30))
        { yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 1500."); yield break; }
        var limits = target.Limits;
        var observations = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != limits.Digest() || observations.Length != 1 ||
            observations[0].RulesDigest != context.Catalog.Digest || observations[0].AssetManifestDigest != context.Assets.Digest || observations[0].LimitsDigest != limits.Digest())
        { yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1500 requires one exact current XML observation bound to rules, assets and limits."); yield break; }

        var findings = new List<ValidationFinding>();
        var work = limits.MaxXmlNodes;
        var characters = limits.MaxXmlCharacters;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in observations[0].Backbones)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) break;
            if (!paths.Add(document.LogicalPath)) duplicates.Add(document.LogicalPath);
        }
        foreach (var document in observations[0].Backbones)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || findings.Count >= remaining) break;
            if (document.ProfileSnapshotId != lifecycle.ProfileSnapshotId || duplicates.Contains(document.LogicalPath) ||
                !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal) ||
                document.Kind is not (BackboneKind.Ich or BackboneKind.UsRegional)) continue;
            var profile = context.Assets.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.Kind == document.Kind);
            if (profile is null) continue;
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            var children = new Dictionary<string, List<ParsedXmlElement>>(StringComparer.Ordinal);
            var roots = 0; ParsedXmlElement? root = null; var unique = true;
            foreach (var element in document.Elements)
            {
                token.ThrowIfCancellationRequested();
                if (work-- <= 0) { unique = false; break; }
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != document.LogicalPath || element.Location.NodePath != element.NodePath) unique = false;
                if (element.ParentPath is not { } parent) { roots++; root = element; }
                else
                {
                    if (!children.TryGetValue(parent, out var siblings)) children.Add(parent, siblings = []);
                    siblings.Add(element);
                }
            }
            if (!unique || roots != 1 || root is null || root.LocalName != profile.RootLocalName || root.NamespaceUri != profile.NamespaceUri) continue;
            foreach (var leaf in document.Elements)
            {
                token.ThrowIfCancellationRequested();
                if (work-- <= 0 || findings.Count >= remaining) break;
                if (leaf is not { LocalName: "leaf", NamespaceUri.Length: 0 } || !Rooted(leaf, root, elements, limits.MaxXmlDepth, ref work, token)) continue;
                var leafId = leaf.Attributes.GetValueOrDefault("ID") is { LocalName: "ID", NamespaceUri: "" } id ? id.Value : null;
                var keywords = leaf.Attributes.GetValueOrDefault("keywords");
                if (keywords is { LocalName: "keywords", NamespaceUri: "", IsDefault: false, ExplicitValue: { } value } &&
                    TooLong(value, ref characters, token))
                    Add(value.Length, leaf.Location with { LeafId = leafId, FieldPath = "@keywords", Line = keywords.Line, Column = keywords.Column });

                ParsedXmlElement? title = null; var titles = 0;
                foreach (var child in children.GetValueOrDefault(leaf.NodePath) ?? [])
                {
                    token.ThrowIfCancellationRequested();
                    if (work-- <= 0) { titles = 0; break; }
                    if (child is { LocalName: "title", NamespaceUri.Length: 0 }) { title = child; titles++; }
                }
                if (titles == 1 && title is not null && title.Children.Count == 0 && !children.ContainsKey(title.NodePath) &&
                    findings.Count < remaining && TooLong(title.Text, ref characters, token))
                    Add(title.Text.Length, title.Location with { LeafId = leafId, FieldPath = "title" });
            }

            void Add(int length, ValidationLocation location)
            {
                if (findings.Count >= remaining) return;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The retained leaf title or explicit keywords value already exceeds 512 characters (printable ASCII or basic CJK).",
                    location with { Files = [new(target.SourceId, document.LogicalPath)] }, Expected: "At most 512 characters",
                    Actual: length.ToString(CultureInfo.InvariantCulture) + " characters (printable ASCII or basic CJK)",
                    RecommendedAction: "Review future title and keyword naming conventions. This check does not truncate or rewrite the submitted value."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            "Only overlength current leaf titles and explicit keywords composed of printable ASCII or basic CJK (U+4E00-U+9FFF) are qualified. These values have equal UTF-16, scalar and grapheme counts. Retained overlength evidence survives incomplete reading; other Unicode counting, other text scopes and complete compliance remain unqualified. No Pass is emitted.", findings);
    }

    private static bool TooLong(string value, ref long characters, CancellationToken token)
    {
        if (value.Length <= 512 || value.Length > characters) return false;
        foreach (var character in value)
        {
            token.ThrowIfCancellationRequested();
            characters--;
            if (character is not (>= ' ' and <= '~') and not (>= '\u4e00' and <= '\u9fff')) return false;
        }
        return true;
    }

    private static bool Rooted(ParsedXmlElement element, ParsedXmlElement root, Dictionary<string, ParsedXmlElement> elements,
        int maxDepth, ref int work, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { element.NodePath }; var depth = 0;
        while (element.ParentPath is { } parent)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || ++depth > maxDepth || !seen.Add(parent) || !elements.TryGetValue(parent, out element!)) return false;
            if (element == root) return true;
            if (element.NamespaceUri.Length != 0) return false;
        }
        return false;
    }
}
