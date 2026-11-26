using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaActivityStartSequenceRule
{
    internal const string RuleId = "FDA47-2025";
    private static readonly ValidationRuleSource M1Specification = new("fda-m1-specification", RuleSourceKind.RegulatorySpecification, "2.6",
        "https://www.fda.gov/media/159382/download?attachment", "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2", "Verified");
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The activity start sequence inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "2025" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return Unknown("Captured FDA criteria and an explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2027, 1, 16)) return new(RuleId, CheckStatus.NotApplicable, "FDA 2025 is not yet effective for the selected date.");
        if (!context.HasArchivedOfficialSource(M1Specification) || context.FdaM1Policy is not { } policy) return Unknown("The captured M1 specification and attribute policy are required.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest()) return Unknown("The lifecycle facts are not bound to this input.");
        var work = target.Limits.MaxXmlNodes;
        var findings = new List<ValidationFinding>();
        try
        {
            var xmls = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections) { Tick(); if (xml.InputDigest == target.InputDigest) xmls.Add(xml); }
            if (xmls.Count != 1) return Unknown("A unique current XML inspection is required.");
            var observation = xmls[0];
            if (observation.ProfileSnapshotId != lifecycle.ProfileSnapshotId || observation.RulesDigest != context.Catalog.Digest || observation.AssetManifestDigest != context.Assets.Digest || observation.LimitsDigest != target.Limits.Digest()) return Unknown("XML context binding is inconsistent.");
            var regionals = new List<ParsedBackbone>();
            foreach (var document in observation.Backbones) { Tick(); if (document.Kind == BackboneKind.UsRegional) regionals.Add(document); }
            if (regionals.Count != 1) return Unknown("A unique regional backbone is required.");
            var regional = regionals[0];
            var sequence = target.Manifest.SequenceNumber;
            if (!regional.ReadComplete || regional.DtdStatus != CheckStatus.Pass || regional.ProfileSnapshotId != observation.ProfileSnapshotId || regional.DocumentTypeName != "fda-regional:fda-regional" || !regional.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal)) return Unknown("Complete qualified regional XML is required.");
            var fileCount = 0; var documentCount = 0;
            foreach (var file in target.Manifest.Files) { Tick(); if (file.LogicalPath == regional.LogicalPath) fileCount++; }
            foreach (var document in observation.Backbones) { Tick(); if (document.LogicalPath == regional.LogicalPath) documentCount++; }
            foreach (var document in observation.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == regional.LogicalPath) documentCount++; }
            if (fileCount != 1 || documentCount != 1) return Unknown("Regional file identity is ambiguous.");
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in regional.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != regional.LogicalPath || element.Location.SequenceNumber != sequence) return Unknown("Element identity is inconsistent.");
            }
            var roots = new List<ParsedXmlElement>();
            foreach (var element in regional.Elements)
            {
                Tick();
                if (element.ParentPath is null) roots.Add(element);
                else if (!elements.TryGetValue(element.ParentPath, out var parent) || !Contains(parent.Children, element.NodePath)) return Unknown("A reverse child relationship is missing.");
                var children = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in element.Children) { Tick(); if (!children.Add(child) || !elements.TryGetValue(child, out var value) || value.ParentPath != element.NodePath) return Unknown("Child relationships are ambiguous."); }
            }
            if (roots.Count != 1 || roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } || roots[0].Attribute("dtd-version") != "3.3") return Unknown("A unique qualified regional root is required.");
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
            while (pending.TryPop(out var path)) { Tick(); if (!visited.Add(path)) return Unknown("The regional tree contains a cycle."); foreach (var child in elements[path].Children) { Tick(); pending.Push(child); } }
            if (visited.Count != elements.Count) return Unknown("All regional elements must connect to the root.");
            var applicationSet = Child(Child(roots[0], "admin"), "application-set");
            if (applicationSet is null) return Unknown("A unique application set is required.");
            var members = new List<(ParsedXmlElement Application, string Type)>();
            var identities = new HashSet<(string Type, string Number)>();
            ParsedXmlElement? primary = null;
            foreach (var path in applicationSet.Children)
            {
                Tick(); var application = elements[path];
                if (application is not { Name: "application", LocalName: "application", NamespaceUri.Length: 0 }) continue;
                var owner = application.Attributes.GetValueOrDefault("application-containing-files");
                if (owner is not { LocalName: "application-containing-files", NamespaceUri: "", IsDefault: false, Value: "true" or "false" })
                    return Unknown("Every member requires an explicit file-ownership declaration.");
                var number = Child(Child(application, "application-information"), "application-number");
                if (number is null || number.Children.Count != 0 || string.IsNullOrWhiteSpace(number.Text) || number.Text != number.Text.Trim())
                    return Unknown("Each member requires an exact application identity.");
                var app = Explicit(number, "application-type");
                if (app is null || !Contains(policy.InputValues("application-type"), app) || !identities.Add((app, number.Text)))
                    return Unknown("Member application identities must be qualified and unique.");
                members.Add((application, app));
                if (owner.Value != "true") continue;
                if (primary is not null) return Unknown("Multiple file-owning applications do not identify one primary input.");
                primary = application;
            }
            if (primary is null) return Unknown("A unique file-owning primary application is required.");
            var primaryOrder = Child(Child(primary, "submission-information"), "sequence-number");
            if (primaryOrder is null || primaryOrder.Children.Count != 0 || primaryOrder.Text != sequence)
                return Unknown("The primary application's sequence must bind the captured directory sequence.");
            var incomplete = false; var applicable = false;
            foreach (var (application, app) in members)
            {
                Tick();
                var submission = Child(application, "submission-information");
                var id = Child(submission, "submission-id"); var order = Child(submission, "sequence-number");
                if (id is null || order is null || id.Children.Count != 0 || order.Children.Count != 0 || !Sequence(order.Text) || !Sequence(id.Text))
                { incomplete = true; continue; }
                var type = Explicit(id, "submission-type"); var subtype = Explicit(order, "submission-sub-type");
                if (type is null || subtype is null || !Contains(policy.InputValues("submission-type"), type) ||
                    !Contains(policy.InputValues("submission-sub-type"), subtype) || !policy.AllowsCombination(app, type, subtype))
                { incomplete = true; continue; }
                if (subtype is "fdasst4" or "fdasst5") continue;
                // Captured M1 Tables 2/4 and III.B.3 qualify these starts, not every application/presubmission.
                var starts = (type is "fdast5" or "fdast9" or "fdast10") && subtype == "fdasst6" ||
                    (type is "fdast7" or "fdast8") && subtype == "fdasst1" || type == "fdast6" && subtype == "fdasst7" || type == "fdast11" && subtype == "fdasst3";
                if (!starts) { incomplete = true; continue; }
                applicable = true;
                if (id.Text == order.Text) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "A qualified new regulatory activity must use that application's own sequence number as submission-id.",
                    id.Location with { FieldPath = "submission-id", Files = [new(target.SourceId, regional.LogicalPath)] }, order.Text, id.Text));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A member's start sequence mismatch is established; other members may remain unqualified.", findings.ToArray()));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
                "Compared each qualified start with its own application sequence without requiring history. Other start combinations and grouped-submission eligibility remain separate qualifications.", findings);

            string? Explicit(ParsedXmlElement element, string name)
            {
                Tick();
                return element.Attributes.GetValueOrDefault(name) is { NamespaceUri: "", IsDefault: false } attribute && attribute.LocalName == name ? attribute.Value : null;
            }

            ParsedXmlElement? Child(ParsedXmlElement? parent, string name)
            {
                Tick(); if (parent is null) return null;
                ParsedXmlElement? found = null;
                foreach (var path in parent.Children) { Tick(); var child = elements[path]; if (child.Name != name || child.LocalName != name || child.NamespaceUri.Length != 0) continue; if (found is not null) return null; found = child; }
                return found;
            }
        }
        catch (StartWorkLimitException)
        {
            return findings.Count > 0 ? new(RuleId, CheckStatus.Fail, "Known member mismatches were retained after the comparison work allowance was exhausted.", findings)
                : Unknown("The activity start comparison work allowance was exhausted.");
        }
        bool Contains(IEnumerable<string> values, string value) { foreach (var candidate in values) { Tick(); if (candidate == value) return true; } return false; }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new StartWorkLimitException(); }
    }
    private static bool Sequence(string value) => value.Length == 4 && value.All(character => character is >= '0' and <= '9');
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class StartWorkLimitException : Exception;
}
