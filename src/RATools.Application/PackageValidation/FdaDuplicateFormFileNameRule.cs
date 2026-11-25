using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaDuplicateFormFileNameRule
{
    internal const string RuleId = "FDA47-5032";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken token, Action<RuleCheckResult>? progress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        progress?.Invoke(Unknown("The duplicate form filename inspection did not complete.")); token.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "5032" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return Unknown("Captured criteria and explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2013, 4, 30)) return new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5032.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest()) return Unknown("The lifecycle binding is inconsistent.");
        var findings = new List<ValidationFinding>(); var work = target.Limits.MaxXmlNodes;
        try
        {
            var xmls = new List<PackageXmlInspection>();
            foreach (var item in lifecycle.XmlInspections) { Tick(); if (item.InputDigest == target.InputDigest) xmls.Add(item); }
            if (xmls.Count != 1) return Unknown("A unique current XML observation is required.");
            var xml = xmls[0];
            if (xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId || xml.RulesDigest != context.Catalog.Digest || xml.AssetManifestDigest != context.Assets.Digest || xml.LimitsDigest != target.Limits.Digest()) return Unknown("XML context binding is inconsistent.");
            var regionals = new List<ParsedBackbone>();
            foreach (var item in xml.Backbones) { Tick(); if (item.Kind == BackboneKind.UsRegional) regionals.Add(item); }
            if (regionals.Count != 1) return Unknown("A unique current regional backbone is required.");
            var regional = regionals[0]; var sequence = target.Manifest.SequenceNumber;
            if (!regional.ReadComplete || regional.DtdStatus != CheckStatus.Pass || regional.ProfileSnapshotId != xml.ProfileSnapshotId ||
                regional.DocumentTypeName != "fda-regional:fda-regional" || !Contains(regional.ResolvedAssets, "us-dtd") ||
                !regional.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal) ||
                !context.Assets.Assets.TryGetValue("us-dtd", out var dtd) || dtd.Sha256 != "d45139785dd6aa7eb3a60a23854ec2ea7d5d17c7c87b9ca12ab268baafc1dbec") return Unknown("Complete fixed-DTD regional XML is required.");
            var files = new Dictionary<string, PackageInputFile>(StringComparer.Ordinal);
            foreach (var file in target.Manifest.Files) { Tick(); if (!files.TryAdd(file.LogicalPath, file)) return Unknown("Captured filenames are ambiguous."); }
            if (!files.ContainsKey(regional.LogicalPath)) return Unknown("Regional capture is absent.");
            var count = 0;
            foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == regional.LogicalPath) count++; }
            foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == regional.LogicalPath) count++; }
            if (count != 1) return Unknown("Regional observations are ambiguous.");
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in regional.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != regional.LogicalPath || element.Location.SequenceNumber != sequence) return Unknown("Element identity is inconsistent.");
            }
            var roots = new List<ParsedXmlElement>();
            foreach (var element in elements.Values)
            {
                Tick();
                if (element.ParentPath is null) roots.Add(element);
                else if (!elements.TryGetValue(element.ParentPath, out var parent) || !Contains(parent.Children, element.NodePath)) return Unknown("Element parents are inconsistent.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in element.Children) { Tick(); if (!seen.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return Unknown("Element children are inconsistent."); }
            }
            if (roots.Count != 1 || roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } || roots[0].Attribute("dtd-version") != "3.3") return Unknown("Regional root identity is unknown.");
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
            while (pending.TryPop(out var path)) { Tick(); if (!visited.Add(path)) return Unknown("Cyclic regional tree."); foreach (var child in elements[path].Children) { Tick(); pending.Push(child); } }
            if (visited.Count != elements.Count) return Unknown("Disconnected regional elements.");
            var leafPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var leaf in regional.Leaves) { Tick(); if (!leafPaths.Add(leaf.NodePath)) return Unknown("Duplicate leaf observations."); }
            var groups = PackageFormBindingReader.Read(target, lifecycle, token);
            var first = new Dictionary<(string Type, string Number, string Marker), string>();
            var reported = new HashSet<(string Type, string Number, string Marker)>();
            foreach (var group in groups.Groups)
            {
                Tick();
                if (group.SourceLogicalPath != regional.LogicalPath || !group.XmlReadComplete || group.DtdStatus != CheckStatus.Pass ||
                    group.Applications.Count != 1 || group.Scope is not ("ApplicationAdmin" or "SharedM1")) continue;
                var app = group.Applications[0];
                if (app.Numbers.Count != 1) continue;
                var number = app.Numbers[0]; var type = number.Type;
                if (number.Number.Length != 6 || number.Number.Any(c => c is < '0' or > '9') || type is not { IsDefault: false, NamespaceUri: "" } ||
                    type.Value is not ("fdaat1" or "fdaat2" or "fdaat3" or "fdaat4")) continue;
                foreach (var leaf in group.Leaves)
                {
                    Tick();
                    if (!elements.TryGetValue(leaf.NodePath, out var element) || element is not { Name: "leaf", LocalName: "leaf", NamespaceUri: "" } ||
                        leaf.Operation is not ("new" or "replace" or "append") || element.Attribute("operation") != leaf.Operation ||
                        leaf.Href is null || element.Attribute("href", "http://www.w3c.org/1999/xlink") != leaf.Href ||
                        leaf.Location.LogicalPath != regional.LogicalPath || leaf.Location.SequenceNumber != sequence) continue;
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(regional.LogicalPath, leaf.Href); }
                    catch (ArgumentException) { continue; }
                    if (reference.ExternalUri is not null || reference.Query is not null || reference.LogicalPath is not { } file ||
                        !file.StartsWith(sequence + "/", StringComparison.Ordinal) || !files.ContainsKey(file)) continue;
                    var name = file[(file.LastIndexOf('/') + 1)..];
                    if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var marker in new[] { "1571", "356h" })
                    {
                        Tick(); if (!name.Contains(marker, StringComparison.Ordinal)) continue;
                        var key = (type.Value, number.Number, marker);
                        if (!first.TryGetValue(key, out var original)) { first.Add(key, file); continue; }
                        if (original == file || reported.Contains(key) || remaining <= 0) continue;
                        reported.Add(key); remaining--;
                        findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                            "More than one distinct current file associated with the same application contains the application-form number in its filename.",
                            leaf.Location with { FieldPath = "@xlink:href", Files = [new(target.SourceId, original), new(target.SourceId, file)] },
                            "Only one file with this form number in its filename per application", $"{type.Value}/{number.Number}; {marker}; {original}; {file}",
                            "In future submissions, include only one file with the form number in its filename; avoid that number in ancillary document filenames."));
                        progress?.Invoke(Result()); token.ThrowIfCancellationRequested();
                    }
                }
            }
        }
        catch (WorkLimitException) { }
        token.ThrowIfCancellationRequested(); return Result();

        RuleCheckResult Result() => new(RuleId, findings.Count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            "Only exact current form-group filenames with a unique application association are counted. Actual form identity, other locations/numbers, unreferenced files, historical lifecycle and shared multi-application scope remain unqualified; absence of a duplicate is not Pass.", findings.ToArray());
        void Tick() { token.ThrowIfCancellationRequested(); if (work-- <= 0) throw new WorkLimitException(); }
        bool Contains(IEnumerable<string> values, string expected) { foreach (var value in values) { Tick(); if (value == expected) return true; } return false; }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class WorkLimitException : Exception;
}
