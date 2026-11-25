using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfAnnotationRule
{
    internal const string RuleId = "FDA47-5055";
    internal sealed class Inspection
    {
        private readonly PackageDeliveryInput _input;
        private readonly CancellationToken _token;
        private readonly Action<RuleCheckResult>? _progress;
        private readonly Dictionary<string, PackageInputFile> _expected = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ValidationLocation> _outside = new(StringComparer.Ordinal);
        private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
        private readonly List<ValidationFinding> _findings = [];
        private readonly ValidationRuleDescriptor? _rule;
        private RuleCheckResult? _terminal;
        private bool _missingReferencedPdf;
        private int _work;
        public int FindingCount => _findings.Count;

        internal Inspection(PackageDeliveryInput input, PackageFileInspection files, PackageLifecycleInspection? lifecycle,
            PackageStandardsContext context, CancellationToken token, Action<RuleCheckResult>? progress = null)
        {
            _input = input; _token = token; _progress = progress; _work = input.Packages.Target.Limits.MaxXmlNodes;
            _rule = context.Catalog.ForProfile(files.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
            if (_rule is null) return;
            _terminal = Unknown("Annotation location evidence or qualification is incomplete.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "5055" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2016, 9, 14))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5055."); return; }
            var target = input.Packages.Target;
            if (files.InputDigest != input.InputDigest || files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
                files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != target.Limits.Digest() || lifecycle is null ||
                lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != files.HistoryManifestDigest || lifecycle.ProfileSnapshotId != files.ProfileSnapshotId ||
                lifecycle.RulesDigest != files.RulesDigest || lifecycle.AssetManifestDigest != files.AssetManifestDigest || lifecycle.LimitsDigest != files.LimitsDigest) return;
            try
            {
                var captured = new HashSet<string>(StringComparer.Ordinal);
                foreach (var file in target.Manifest.Files)
                {
                    Tick(); if (!captured.Add(file.LogicalPath)) return;
                    if (file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) _expected.Add(file.LogicalPath, file);
                }
                var xmls = new List<PackageXmlInspection>();
                foreach (var item in lifecycle.XmlInspections) { Tick(); if (item.InputDigest == target.InputDigest) xmls.Add(item); }
                if (xmls.Count != 1) return;
                var xml = xmls[0];
                if (xml.ProfileSnapshotId != files.ProfileSnapshotId || xml.RulesDigest != files.RulesDigest || xml.AssetManifestDigest != files.AssetManifestDigest || xml.LimitsDigest != files.LimitsDigest) return;
                var regionals = new List<ParsedBackbone>();
                foreach (var item in xml.Backbones) { Tick(); if (item.Kind == BackboneKind.UsRegional) regionals.Add(item); }
                if (regionals.Count != 1) return;
                var regional = regionals[0]; var sequence = target.Manifest.SequenceNumber;
                if (!regional.ReadComplete || regional.DtdStatus != CheckStatus.Pass || regional.ProfileSnapshotId != xml.ProfileSnapshotId ||
                    regional.DocumentTypeName != "fda-regional:fda-regional" || !Contains(regional.ResolvedAssets, "us-dtd") ||
                    !regional.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal) || !captured.Contains(regional.LogicalPath) ||
                    !context.Assets.Assets.TryGetValue("us-dtd", out var dtd) || dtd.Sha256 != "d45139785dd6aa7eb3a60a23854ec2ea7d5d17c7c87b9ca12ab268baafc1dbec") return;
                var count = 0;
                foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == regional.LogicalPath) count++; }
                foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == regional.LogicalPath) count++; }
                if (count != 1) return;
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                foreach (var element in regional.Elements)
                {
                    Tick();
                    if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != regional.LogicalPath || element.Location.SequenceNumber != sequence) return;
                }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in elements.Values)
                {
                    Tick();
                    if (element.ParentPath is null) roots.Add(element);
                    else if (!elements.TryGetValue(element.ParentPath, out var parent) || !Contains(parent.Children, element.NodePath)) return;
                    var children = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var child in element.Children) { Tick(); if (!children.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return; }
                }
                if (roots.Count != 1 || roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } || roots[0].Attribute("dtd-version") != "3.3") return;
                var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
                while (pending.TryPop(out var path)) { Tick(); if (!visited.Add(path)) return; foreach (var child in elements[path].Children) { Tick(); pending.Push(child); } }
                if (visited.Count != elements.Count) return;
                var leaves = new HashSet<string>(StringComparer.Ordinal);
                foreach (var leaf in regional.Leaves) { Tick(); if (!leaves.Add(leaf.NodePath)) return; }
                foreach (var leaf in regional.Leaves)
                {
                    Tick();
                    if (!elements.TryGetValue(leaf.NodePath, out var element) || element is not { Name: "leaf", LocalName: "leaf", NamespaceUri: "" } ||
                        leaf.Operation is not ("new" or "replace" or "append") || element.Attribute("operation") != leaf.Operation ||
                        leaf.Href is null || element.Attribute("href", "http://www.w3c.org/1999/xlink") != leaf.Href ||
                        leaf.Location.LogicalPath != regional.LogicalPath || leaf.Location.SequenceNumber != sequence || leaf.Location.NodePath != leaf.NodePath) continue;
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(regional.LogicalPath, leaf.Href); }
                    catch (ArgumentException) { continue; }
                    if (reference.ExternalUri is not null || reference.Query is not null || reference.LogicalPath is not { } file) continue;
                    if (!_expected.ContainsKey(file))
                    {
                        if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) _missingReferencedPdf = true;
                        continue;
                    }
                    var promotional = false; var module = false; var qualified = true;
                    for (var parent = element.ParentPath; parent is not null; parent = elements[parent].ParentPath)
                    {
                        Tick(); var ancestor = elements[parent];
                        if (ancestor.ParentPath is null) continue;
                        if (ancestor.NamespaceUri.Length != 0 || ancestor.Name != ancestor.LocalName) { qualified = false; break; }
                        if (ancestor.Name == "m1-regional" && ancestor.ParentPath == roots[0].NodePath) module = true;
                        if (ancestor.Name == "m1-15-promotional-material" && ancestor.ParentPath is { } owner &&
                            elements[owner].Name == "m1-regional" && elements[owner].ParentPath == roots[0].NodePath) promotional = true;
                    }
                    if (qualified && module && !promotional) _outside.TryAdd(file, leaf.Location);
                }
                _work = target.Limits.MaxPdfObjects;
                _terminal = null;
            }
            catch (LocationLimitException) { }
        }

        internal void Observe(PackagePdfDocument document, int remaining)
        {
            if (_rule is null || _terminal is not null) return;
            _token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
            if (!_outside.TryGetValue(document.LogicalPath, out var location) || !_observed.Add(document.LogicalPath) ||
                document.CapturedSha256 != _expected[document.LogicalPath].Sha256 || document.Facts is not { } facts ||
                facts.ContentProcessing != PdfContentProcessingStatus.Processed || facts.Basics.ParseError is not null ||
                facts.Basics.PageCount is null or < 1 || facts.Basics.IsEncrypted) return;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var annotation in facts.MarkupAnnotations)
            { _token.ThrowIfCancellationRequested(); if (_work-- <= 0 || !paths.Add(annotation.ObjectPath)) return; }
            foreach (var annotation in facts.MarkupAnnotations)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0 || remaining <= 0) return;
                if (annotation.PageNumber < 1 || annotation.PageNumber > facts.Basics.PageCount || string.IsNullOrWhiteSpace(annotation.ObjectPath) ||
                    annotation.Subtype is not ("Text" or "FreeText" or "Highlight" or "Underline" or "StrikeOut" or "Squiggly" or
                        "Stamp" or "Ink" or "Line" or "Square" or "Circle" or "Polygon" or "PolyLine" or "Caret")) continue;
                _findings.Add(new(RuleId, CheckStatus.Fail, _rule.Severity, RuleId,
                    "A current PDF referenced outside Module 1 section 1.15 contains an identified markup annotation.",
                    location with { FieldPath = annotation.ObjectPath, Files = [new(_input.Packages.Target.SourceId, document.LogicalPath), new(_input.Packages.Target.SourceId, location.LogicalPath!)] },
                    "No markup annotations outside section 1.15", annotation.Subtype,
                    "Ensure documents in future submissions do not have annotations outside Module 1 section 1.15."));
                _progress?.Invoke(new(RuleId, CheckStatus.Fail, "An annotation outside the qualified promotional exception is established.", _findings.ToArray()));
                _token.ThrowIfCancellationRequested(); return; // One located finding per physical current PDF.
            }
        }

        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : !interrupted && !_missingReferencedPdf && _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.NotEvaluated,
                "Only identified markup in current PDFs with qualified regional M1 references outside section 1.15 is checked. Links/widgets are not treated as markup; other annotation semantics, modules 2-5, historical/unreferenced contents and unknown ownership remain unqualified; no Pass is emitted.", _findings.ToArray());
        }
        private void Tick() { _token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new LocationLimitException(); }
        private bool Contains(IEnumerable<string> values, string expected) { foreach (var value in values) { Tick(); if (value == expected) return true; } return false; }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class LocationLimitException : Exception;
}
