using RATools.Application.Ctd;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaEmptyLowestHeadingRule
{
    internal const string RuleId = "FDA47-3078";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The lowest standard heading inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "3078" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return Unknown("Captured FDA criteria and an explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2013, 4, 30)) return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 3078.");
        const string dtdHash = "c094aa2bded99564ade8ac78ae1540f95e518461b8eebe9a3063f67a165c2731";
        if (context.NodeSchema.Version != IchSectionDefinitions.Version || context.NodeSchema.SourceSha256 != dtdHash ||
            !context.Assets.Assets.TryGetValue("ich-dtd", out var asset) || asset.Sha256 != dtdHash) return Unknown("The fixed ICH heading structure and DTD are required.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest()) return Unknown("The lifecycle facts are not bound to this input.");
        var work = target.Limits.MaxXmlNodes;
        var findings = new List<ValidationFinding>(); var incomplete = false; var applicable = false;
        try
        {
            var xmls = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections) { Tick(); if (xml.InputDigest == target.InputDigest) xmls.Add(xml); }
            if (xmls.Count != 1) return Unknown("A unique current XML inspection is required.");
            var observation = xmls[0];
            if (observation.ProfileSnapshotId != lifecycle.ProfileSnapshotId || observation.RulesDigest != context.Catalog.Digest || observation.AssetManifestDigest != context.Assets.Digest || observation.LimitsDigest != target.Limits.Digest()) return Unknown("XML context binding is inconsistent.");
            var path = target.Manifest.SequenceNumber + "/index.xml";
            var files = 0; foreach (var file in target.Manifest.Files) { Tick(); if (file.LogicalPath == path) files++; }
            var indices = new List<ParsedBackbone>();
            foreach (var document in observation.Backbones) { Tick(); if (document.LogicalPath == path) indices.Add(document); }
            if (files != 1 || indices.Count != 1) return Unknown("A unique captured current index is required.");
            var index = indices[0];
            if (index.Kind != BackboneKind.Ich || index.ProfileSnapshotId != observation.ProfileSnapshotId || !index.ReadComplete || index.DtdStatus != CheckStatus.Pass ||
                index.DocumentTypeName != "ectd:ectd" || !Contains(index.ResolvedAssets, "ich-dtd")) return Unknown("A complete qualified ICH backbone is required to establish leaf absence.");
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in index.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != path || element.Location.SequenceNumber != target.Manifest.SequenceNumber) return Unknown("Element identities conflict.");
            }
            var roots = new List<ParsedXmlElement>();
            foreach (var element in index.Elements)
            {
                Tick();
                if (element.ParentPath is null) roots.Add(element);
                else if (!elements.TryGetValue(element.ParentPath, out var parent) || !Contains(parent.Children, element.NodePath)) return Unknown("A reverse child relationship is missing.");
                var members = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in element.Children) { Tick(); if (!members.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return Unknown("Child relationships conflict."); }
            }
            if (roots.Count != 1 || roots[0] is not { Name: "ectd:ectd", LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" } || roots[0].Attribute("dtd-version") != "3.2") return Unknown("A unique qualified ICH root is required.");
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
            while (pending.TryPop(out var current)) { Tick(); if (!visited.Add(current)) return Unknown("The index tree contains a cycle."); foreach (var child in elements[current].Children) { Tick(); pending.Push(child); } }
            if (visited.Count != elements.Count) return Unknown("All observed elements must connect to the actual index root.");
            foreach (var element in index.Elements)
            {
                Tick();
                if (element.NamespaceUri.Length != 0 || element.Name != element.LocalName || !context.NodeSchema.Definitions.TryGetValue(element.Name, out var definition) ||
                    definition.Kind != CtdNodeKind.Standard || definition.SectionPath is not { Length: >= 2 } section || section[0] != 'm' || section[1] is < '2' or > '5') continue;
                if (!QualifiedChain(element, definition)) { incomplete = true; continue; }
                if (definition.Children.Count != 0 || !definition.AllowsLeaves) continue;
                var leaves = 0; var unsupported = false;
                foreach (var childPath in element.Children)
                {
                    Tick(); var child = elements[childPath];
                    if (child is { Name: "leaf", LocalName: "leaf", NamespaceUri.Length: 0 }) leaves++;
                    else unsupported = true;
                }
                if (unsupported) { incomplete = true; continue; }
                applicable = true;
                if (leaves != 0) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "A lowest standard ICH heading contains no leaf elements.",
                    element.Location with { FieldPath = "leaf", Files = [new(target.SourceId, path)] }, "At least one leaf element", "0 leaf elements",
                    "No corrective action is necessary. Update SOPs so lowest-level headings contain leaf elements."));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An empty lowest standard heading is established; other heading interpretations may remain unqualified.", findings.ToArray()));
            }
            bool QualifiedChain(ParsedXmlElement element, SectionDefinition definition)
            {
                var current = element;
                while (definition.ParentDefinitionKey is { } key)
                {
                    Tick();
                    if (current.ParentPath is not { } parent || !elements.TryGetValue(parent, out var next) || next.Name != key || next.LocalName != key || next.NamespaceUri.Length != 0) return false;
                    current = next; definition = context.NodeSchema.Get(key);
                }
                return current.ParentPath == roots[0].NodePath;
            }
        }
        catch (HeadingWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete || !applicable ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked only lowest fixed ICH standard headings without extension children. Regional headings, extension-instance semantics and parent headings lacking lower instances remain unqualified. Existing leaf elements include delete leaves.", findings);
        bool Contains(IEnumerable<string> values, string value) { foreach (var candidate in values) { Tick(); if (candidate == value) return true; } return false; }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new HeadingWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class HeadingWorkLimitException : Exception;
}
