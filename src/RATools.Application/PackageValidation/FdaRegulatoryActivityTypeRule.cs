using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRegulatoryActivityTypeRule
{
    internal const string RuleId = "FDA47-2024";
    private static readonly ValidationRuleSource M1Specification = new("fda-m1-specification", RuleSourceKind.RegulatorySpecification, "2.6",
        "https://www.fda.gov/media/159382/download?attachment", "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2", "Verified");
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The selected parent activity comparison did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "2024" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("Captured FDA criteria and an explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2027, 1, 16)) return new(RuleId, CheckStatus.NotApplicable, "FDA 2024 is not yet effective for the selected date.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest())
            return Unknown("The lifecycle inspection is not bound to this input and context.");
        var work = target.Limits.MaxXmlNodes;
        try
        {
            var current = Activity(target, context, lifecycle.ProfileSnapshotId);
            if (current is null || !Sequence(current.SubmissionId) || string.CompareOrdinal(current.SubmissionId, target.Manifest.SequenceNumber) >= 0)
                return Unknown("A unique child activity with an earlier parent sequence is required.");
            var entries = new List<HistoryBaselineEntry>();
            foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == current.SubmissionId) entries.Add(entry); }
            if (entries.Count != 1 || !packages.Sequences.TryGetValue(current.SubmissionId, out var parentInput)) return Unknown("The declared parent sequence is not selected.");
            var parentContext = context.ExplicitHistoryForSequence(current.SubmissionId);
            var baseline = entries[0];
            if (parentContext is null || baseline.StandardsSnapshotId is null || parentContext.SnapshotId != baseline.StandardsSnapshotId ||
                baseline.ProfileSnapshotId != lifecycle.ProfileSnapshotId || baseline.ContentDigest != parentInput.InputDigest || baseline.SourceId != parentInput.SourceId)
                return Unknown("The parent requires an explicitly selected matching standards snapshot and capture.");
            var parent = Activity(parentInput, parentContext, baseline.ProfileSnapshotId);
            if (parent is null || parent.SubmissionId != current.SubmissionId || parent.ApplicationNumber != current.ApplicationNumber || parent.ApplicationType != current.ApplicationType)
                return Unknown("The selected parent is not a unique self-identifying activity for the same XML application.");
            if (current.SubmissionType == parent.SubmissionType)
                return current.Grouped || parent.Grouped
                    ? Unknown("The selected primary application pair has matching types; other grouped members and full activity history remain unqualified.")
                    : new(RuleId, CheckStatus.Pass, "The selected current/parent pair has matching types; this is not full activity history or regulatory-received-version qualification.");
            if (remaining <= 0) return Unknown("No finding allowance remains for the established mismatch.");
            var finding = new ValidationFinding(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                "The current sequence's submission type differs from its explicitly selected, self-identifying parent activity.",
                current.Location with { FieldPath = "@submission-type", Files = [new(target.SourceId, current.Location.LogicalPath!), new(parentInput.SourceId, parent.Location.LogicalPath!, true)] },
                parent.SubmissionType, current.SubmissionType);
            var result = new RuleCheckResult(RuleId, CheckStatus.Fail, "A selected parent/current type mismatch is established; wider activity qualification remains incomplete.", [finding]);
            retainProgress?.Invoke(result);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (ActivityWorkLimitException) { return Unknown("The activity comparison work allowance was exhausted."); }

        ActivityFact? Activity(ICapturedPackageInput input, PackageStandardsContext selected, string profile)
        {
            Tick();
            if (profile != PackageValidationCatalog.UsProfile || !selected.HasArchivedOfficialSource(M1Specification) || selected.FdaM1Policy is not { } policy) return null;
            var xmls = new List<PackageXmlInspection>();
            foreach (var xml in lifecycle.XmlInspections) { Tick(); if (xml.InputDigest == input.InputDigest) xmls.Add(xml); }
            if (xmls.Count != 1) return null;
            var observation = xmls[0];
            if (observation.ProfileSnapshotId != profile || observation.RulesDigest != selected.Catalog.Digest || observation.AssetManifestDigest != selected.Assets.Digest || observation.LimitsDigest != input.Limits.Digest()) return null;
            var regionals = new List<ParsedBackbone>();
            foreach (var document in observation.Backbones) { Tick(); if (document.Kind == BackboneKind.UsRegional) regionals.Add(document); }
            if (regionals.Count != 1) return null;
            var regional = regionals[0];
            var sequence = input.Manifest.SequenceNumber;
            if (!regional.ReadComplete || regional.DtdStatus != CheckStatus.Pass || regional.ProfileSnapshotId != profile ||
                !regional.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal) || regional.DocumentTypeName != "fda-regional:fda-regional") return null;
            var files = 0; var samePath = 0;
            foreach (var file in input.Manifest.Files) { Tick(); if (file.LogicalPath == regional.LogicalPath) files++; }
            foreach (var document in observation.Backbones) { Tick(); if (document.LogicalPath == regional.LogicalPath) samePath++; }
            foreach (var document in observation.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == regional.LogicalPath) samePath++; }
            if (files != 1 || samePath != 1) return null;
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in regional.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != regional.LogicalPath || element.Location.SequenceNumber != sequence) return null;
            }
            var roots = new List<ParsedXmlElement>();
            foreach (var element in regional.Elements)
            {
                Tick();
                if (element.ParentPath is null) roots.Add(element);
                else if (!elements.TryGetValue(element.ParentPath, out var parent) || !Contains(parent.Children, element.NodePath)) return null;
                var children = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in element.Children) { Tick(); if (!children.Add(path) || !elements.TryGetValue(path, out var child) || child.ParentPath != element.NodePath) return null; }
            }
            if (roots.Count != 1 || roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } || roots[0].Attribute("dtd-version") != "3.3") return null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(); pending.Push(roots[0].NodePath);
            while (pending.TryPop(out var path)) { Tick(); if (!visited.Add(path)) return null; foreach (var child in elements[path].Children) { Tick(); pending.Push(child); } }
            if (visited.Count != elements.Count) return null;
            var admin = Child(roots[0], "admin");
            var applicationSet = Child(admin, "application-set");
            if (applicationSet is null) return null;
            ParsedXmlElement? application = null;
            var identities = new HashSet<(string Type, string Number)>();
            foreach (var path in applicationSet.Children)
            {
                Tick();
                var member = elements[path];
                if (member is not { Name: "application", LocalName: "application", NamespaceUri.Length: 0 }) continue;
                var owner = Original(member, "application-containing-files");
                if (owner is not ("true" or "false")) return null;
                var memberNumber = Child(Child(member, "application-information"), "application-number");
                if (memberNumber is null || memberNumber.Children.Count != 0 || string.IsNullOrWhiteSpace(memberNumber.Text) ||
                    memberNumber.Text != memberNumber.Text.Trim()) return null;
                var memberType = Original(memberNumber, "application-type");
                if (memberType is null || !Contains(policy.InputValues("application-type"), memberType) ||
                    !identities.Add((memberType, memberNumber.Text))) return null;
                if (owner != "true") continue;
                if (application is not null) return null;
                application = member;
            }
            if (application is null) return null;
            var number = Child(Child(application, "application-information"), "application-number");
            var submission = Child(application, "submission-information");
            var id = Child(submission, "submission-id");
            var order = Child(submission, "sequence-number");
            if (number is null || id is null || order is null || number.Children.Count != 0 || id.Children.Count != 0 || order.Children.Count != 0 ||
                string.IsNullOrWhiteSpace(number.Text) || number.Text != number.Text.Trim() || order.Text != sequence || !Sequence(id.Text)) return null;
            var applicationType = Original(number, "application-type");
            var submissionType = Original(id, "submission-type");
            if (applicationType is null || submissionType is null || !Contains(policy.InputValues("application-type"), applicationType) || !Contains(policy.InputValues("submission-type"), submissionType)) return null;
            return new(number.Text, applicationType, id.Text, submissionType, id.Location, identities.Count > 1);

            string? Original(ParsedXmlElement element, string name)
            {
                var count = 0; string? value = null;
                foreach (var attribute in element.Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName != name || attribute.NamespaceUri.Length != 0) continue;
                    count++;
                    if (!attribute.IsDefault && attribute.Value == attribute.ExplicitValue) value = attribute.ExplicitValue;
                }
                return count == 1 ? value : null;
            }

            ParsedXmlElement? Child(ParsedXmlElement? parent, string name)
            {
                Tick();
                if (parent is null) return null;
                ParsedXmlElement? found = null;
                foreach (var path in parent.Children)
                {
                    Tick(); var child = elements[path];
                    if (child.Name != name || child.LocalName != name || child.NamespaceUri.Length != 0) continue;
                    if (found is not null) return null;
                    found = child;
                }
                return found;
            }
        }
        bool Contains(IEnumerable<string> values, string value) { foreach (var candidate in values) { Tick(); if (candidate == value) return true; } return false; }
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new ActivityWorkLimitException(); }
    }
    private static bool Sequence(string value) => value.Length == 4 && value.All(character => character is >= '0' and <= '9');
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed record ActivityFact(string ApplicationNumber, string ApplicationType, string SubmissionId, string SubmissionType, ValidationLocation Location, bool Grouped);
    private sealed class ActivityWorkLimitException : Exception;
}
