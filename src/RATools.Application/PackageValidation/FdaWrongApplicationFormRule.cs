using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaWrongApplicationFormRule
{
    internal const string RuleId = "FDA47-5038";
    private static readonly ValidationRuleSource M1Specification = new("fda-m1-specification", RuleSourceKind.RegulatorySpecification, "2.6",
        "https://www.fda.gov/media/159382/download?attachment", "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2", "Verified");
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken token, Action<RuleCheckResult>? progress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        progress?.Invoke(Unknown("The wrong application form inspection did not complete.")); token.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "5038" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return Unknown("Captured FDA criteria and explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2013, 4, 30)) return new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5038.");
        if (context.FdaM1Policy is not { } policy) return Unknown("Captured application and submission dictionaries are required.");
        var expandedNonannualScope = context.SelectionDate >= new DateOnly(2025, 3, 31) && context.HasArchivedOfficialSource(M1Specification);
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest()) return Unknown("Lifecycle binding is inconsistent.");
        var work = target.Limits.MaxXmlNodes; var findings = new List<ValidationFinding>();
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
            if (!regional.ReadComplete || regional.DtdStatus != CheckStatus.Pass || regional.ProfileSnapshotId != xml.ProfileSnapshotId || regional.DocumentTypeName != "fda-regional:fda-regional" ||
                !Contains(regional.ResolvedAssets, "us-dtd") || !regional.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal) ||
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
                var children = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in element.Children) { Tick(); if (!children.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return Unknown("Element children are inconsistent."); }
            }
            if (roots.Count != 1 || roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } || roots[0].Attribute("dtd-version") != "3.3") return Unknown("Regional root identity is unknown.");
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
            while (pending.TryPop(out var path)) { Tick(); if (!visited.Add(path)) return Unknown("Cyclic regional tree."); foreach (var child in elements[path].Children) { Tick(); pending.Push(child); } }
            if (visited.Count != elements.Count) return Unknown("Disconnected regional elements.");
            var leafPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var leaf in regional.Leaves) { Tick(); if (!leafPaths.Add(leaf.NodePath)) return Unknown("Duplicate leaf observations."); }
            var groups = PackageFormBindingReader.Read(target, lifecycle, token);
            var reported = new HashSet<(string Type, string Number, string File)>();
            foreach (var group in groups.Groups)
            {
                Tick();
                if (group.SourceLogicalPath != regional.LogicalPath || !group.XmlReadComplete || group.DtdStatus != CheckStatus.Pass ||
                    group.Scope is not ("ApplicationAdmin" or "SharedM1") || group.Applications.Count != 1) continue;
                var application = group.Applications[0];
                if (application.Numbers.Count != 1) continue;
                var number = application.Numbers[0]; var type = number.Type;
                if (number.Number.Length != 6 || number.Number.Any(c => c is < '0' or > '9') || type is not { IsDefault: false, NamespaceUri: "" } ||
                    !elements.TryGetValue(number.NodePath, out var numberElement) || Explicit(numberElement, "application-type") != type.Value ||
                    !Contains(policy.InputValues("application-type"), type.Value)) continue;
                var nonannualStart = false;
                if (elements.TryGetValue(application.NodePath, out var app))
                {
                    var submission = Child(app, "submission-information");
                    var id = Child(submission, "submission-id"); var order = Child(submission, "sequence-number");
                    var submissionType = id is null ? null : Explicit(id, "submission-type");
                    var subtype = order is null ? null : Explicit(order, "submission-sub-type");
                    // The explicit original-application sequence is independently not
                    // an annual report. Unknown/follow-up activity is not its negation.
                    nonannualStart = id is { Children.Count: 0 } && order is { Children.Count: 0 } && id.Text == sequence && order.Text == sequence &&
                        submissionType == "fdast1" && subtype == "fdasst3" && policy.AllowsCombination(type.Value, submissionType, subtype);
                    if (!nonannualStart && expandedNonannualScope && id is { Children.Count: 0 } && order is { Children.Count: 0 } &&
                        id.Text == sequence && order.Text == sequence)
                    {
                        // Captured M1 Tables 2/4 and III.B.3 independently identify
                        // these starts. Annual reports and follow-ups are not negated.
                        nonannualStart = submissionType is not null && subtype is not null &&
                            policy.AllowsCombination(type.Value, submissionType, subtype) &&
                            ((submissionType is "fdast9" or "fdast10") && subtype == "fdasst6" ||
                             (submissionType is "fdast7" or "fdast8") && subtype == "fdasst1" ||
                             submissionType == "fdast6" && subtype == "fdasst7" || submissionType == "fdast11" && subtype == "fdasst3");
                    }
                }
                foreach (var leaf in group.Leaves)
                {
                    Tick();
                    if (!elements.TryGetValue(leaf.NodePath, out var element) || element is not { Name: "leaf", LocalName: "leaf", NamespaceUri: "" } ||
                        leaf.Operation is not ("new" or "replace" or "append") || Explicit(element, "operation") != leaf.Operation ||
                        leaf.Href is null || Explicit(element, "href", "http://www.w3c.org/1999/xlink") != leaf.Href ||
                        leaf.Location.LogicalPath != regional.LogicalPath || leaf.Location.SequenceNumber != sequence) continue;
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(regional.LogicalPath, leaf.Href); }
                    catch (ArgumentException) { continue; }
                    if (reference.ExternalUri is not null || reference.Query is not null || reference.LogicalPath is not { } file ||
                        !file.StartsWith(sequence + "/", StringComparison.Ordinal) || !files.ContainsKey(file)) continue;
                    var name = file[(file.LastIndexOf('/') + 1)..];
                    var wrong = name == "1571.pdf" && type.Value is "fdaat1" or "fdaat2" or "fdaat3" ||
                        name == "356h.pdf" && type.Value == "fdaat4" || name == "2252.pdf" && nonannualStart;
                    if (!wrong || remaining <= 0 || !reported.Add((type.Value, number.Number, file))) continue;
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "The exact application-form filename is inconsistent with its associated application type or qualified nonannual starting sequence.",
                        leaf.Location with { FieldPath = "@xlink:href", Files = [new(target.SourceId, file)] },
                        "A correctly named application form for the associated application and submission", $"{type.Value}/{number.Number}; {name}",
                        "Resubmit with the correct application form; FDA permits the original sequence number. Update SOPs to use the correct form, filename and eCTD location."));
                    progress?.Invoke(Result()); token.ThrowIfCancellationRequested();
                }
            }

            string? Explicit(ParsedXmlElement element, string name, string namespaceUri = "")
            {
                ParsedXmlAttributeValue? selected = null;
                foreach (var attribute in element.Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName != name || attribute.NamespaceUri != namespaceUri) continue;
                    if (selected is not null) return null;
                    selected = attribute;
                }
                return selected is { IsDefault: false, ExplicitValue: not null } && selected.ExplicitValue == selected.Value ? selected.Value : null;
            }

            ParsedXmlElement? Child(ParsedXmlElement? parent, string name)
            {
                Tick(); if (parent is null) return null;
                ParsedXmlElement? match = null;
                foreach (var path in parent.Children) { Tick(); var child = elements[path]; if (child.Name != name || child.LocalName != name || child.NamespaceUri.Length != 0) continue; if (match is not null) return null; match = child; }
                return match;
            }
        }
        catch (WorkLimitException) { }
        token.ThrowIfCancellationRequested(); return Result();

        RuleCheckResult Result() => new(RuleId, findings.Count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            "Checked only explicit exact-filename contradictions in current qualified form groups, including six additional nonannual starts under captured M1 2.6 from 2025-03-31. Other filenames, actual form identity, other nonannual/follow-up sequences, historical lifecycle, other locations and shared multi-application groups remain unqualified; absence of a contradiction is not Pass.", findings.ToArray());
        void Tick() { token.ThrowIfCancellationRequested(); if (work-- <= 0) throw new WorkLimitException(); }
        bool Contains(IEnumerable<string> values, string expected) { foreach (var value in values) { Tick(); if (value == expected) return true; } return false; }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class WorkLimitException : Exception;
}
