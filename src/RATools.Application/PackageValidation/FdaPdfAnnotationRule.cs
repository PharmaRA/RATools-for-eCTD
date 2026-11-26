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
        private bool _locationsComplete;
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
                var regionals = new List<ParsedBackbone>(); var indexes = new List<ParsedBackbone>();
                foreach (var item in xml.Backbones)
                {
                    Tick();
                    if (item.Kind == BackboneKind.UsRegional) regionals.Add(item);
                    if (item.Kind == BackboneKind.Ich) indexes.Add(item);
                }
                _locationsComplete = regionals.Count == 1 && indexes.Count == 1;
                if (regionals.Count == 1) _locationsComplete &= ReadLocations(regionals[0], false);
                if (indexes.Count == 1) _locationsComplete &= ReadLocations(indexes[0], true);
                _work = target.Limits.MaxPdfObjects;
                _terminal = null;

                bool ReadLocations(ParsedBackbone regional, bool ich)
                {
                var sequence = target.Manifest.SequenceNumber;
                var assetId = ich ? "ich-dtd" : "us-dtd";
                if (!regional.ReadComplete || regional.DtdStatus != CheckStatus.Pass || regional.ProfileSnapshotId != xml.ProfileSnapshotId ||
                    regional.DocumentTypeName != (ich ? "ectd:ectd" : "fda-regional:fda-regional") || !Contains(regional.ResolvedAssets, assetId) ||
                    !regional.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal) || !captured.Contains(regional.LogicalPath) ||
                    !context.Assets.Assets.TryGetValue(assetId, out var dtd) || dtd.Sha256 != (ich
                        ? "c094aa2bded99564ade8ac78ae1540f95e518461b8eebe9a3063f67a165c2731"
                        : "d45139785dd6aa7eb3a60a23854ec2ea7d5d17c7c87b9ca12ab268baafc1dbec")) return false;
                var count = 0;
                foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == regional.LogicalPath) count++; }
                foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == regional.LogicalPath) count++; }
                if (count != 1) return false;
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                foreach (var element in regional.Elements)
                {
                    Tick();
                    if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != regional.LogicalPath || element.Location.SequenceNumber != sequence || element.Location.NodePath != element.NodePath) return false;
                }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in elements.Values)
                {
                    Tick();
                    if (element.ParentPath is null) roots.Add(element);
                    else if (!elements.TryGetValue(element.ParentPath, out var parent) || !Contains(parent.Children, element.NodePath)) return false;
                    var children = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var child in element.Children) { Tick(); if (!children.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return false; }
                }
                if (roots.Count != 1) return false;
                if (ich ? roots[0] is not { Name: "ectd:ectd", LocalName: "ectd", NamespaceUri: "http://www.ich.org/ectd" }
                    : roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } || roots[0].Attribute("dtd-version") != "3.3") return false;
                var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
                while (pending.TryPop(out var path)) { Tick(); if (!visited.Add(path)) return false; foreach (var child in elements[path].Children) { Tick(); pending.Push(child); } }
                if (visited.Count != elements.Count) return false;
                var leaves = new HashSet<string>(StringComparer.Ordinal);
                foreach (var leaf in regional.Leaves) { Tick(); if (!leaves.Add(leaf.NodePath)) return false; }
                var complete = true;
                foreach (var leaf in regional.Leaves)
                {
                    Tick();
                    if (!elements.TryGetValue(leaf.NodePath, out var element) || element is not { Name: "leaf", LocalName: "leaf", NamespaceUri: "" } ||
                        element.Attribute("operation") != leaf.Operation ||
                        leaf.Location.LogicalPath != regional.LogicalPath || leaf.Location.SequenceNumber != sequence || leaf.Location.NodePath != leaf.NodePath)
                    { complete = false; continue; }
                    if (leaf.Operation == "delete") continue;
                    if (leaf.Operation is not ("new" or "replace" or "append") ||
                        leaf.Href is null || element.Attribute("href", "http://www.w3c.org/1999/xlink") != leaf.Href ||
                        leaf.Id != element.Attribute("ID") || leaf.Location.LeafId != leaf.Id)
                    { complete = false; continue; }
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(regional.LogicalPath, leaf.Href); }
                    catch (ArgumentException) { complete = false; continue; }
                    if (reference.ExternalUri is not null || reference.Query is not null || reference.LogicalPath is not { } file) { complete = false; continue; }
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
                        if (ancestor.ParentPath == roots[0].NodePath && (ich
                            ? ancestor.Name is "m2-common-technical-document-summaries" or "m3-quality" or "m4-nonclinical-study-reports" or "m5-clinical-study-reports"
                            : ancestor.Name == "m1-regional")) module = true;
                        if (!ich && ancestor.Name == "m1-15-promotional-material" && ancestor.ParentPath is { } owner &&
                            elements[owner].Name == "m1-regional" && elements[owner].ParentPath == roots[0].NodePath) promotional = true;
                    }
                    if (qualified && module && !promotional) _outside.TryAdd(file, leaf.Location);
                    if (!qualified) complete = false;
                }
                return complete;
                }
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
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : !interrupted && _locationsComplete && !_missingReferencedPdf && _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.NotEvaluated,
                "Identified markup in current PDFs with qualified regional M1 references outside section 1.15 or actual ICH module 2-5 references is checked. Physical folders do not determine the exception, and an allowed reference does not exempt another outside reference. Links/widgets are not markup; other annotation semantics, historical/unreferenced contents and unknown ownership remain unqualified; no Pass is emitted.", _findings.ToArray());
        }
        private void Tick() { _token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new LocationLimitException(); }
        private bool Contains(IEnumerable<string> values, string expected) { foreach (var value in values) { Tick(); if (value == expected) return true; } return false; }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class LocationLimitException : Exception;
}
