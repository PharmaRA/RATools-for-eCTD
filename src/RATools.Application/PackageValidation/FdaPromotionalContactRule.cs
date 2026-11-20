using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPromotionalContactRule
{
    internal const string RuleId = "FDA47-1545";

    internal static RuleCheckResult? Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The promotional contact inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1545" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1545 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2015, 6, 15))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1545.");
        if (context.FdaM1Policy is not { } policy)
            return Unknown("The captured M1 specification, code dictionaries and their qualified mapping are required.");
        var submissionTypes = policy.InputValues("submission-type").ToHashSet(StringComparer.Ordinal);
        var contactTypes = policy.InputValues("applicant-contact-type").ToHashSet(StringComparer.Ordinal);
        if (!submissionTypes.Contains("fdast8") || !contactTypes.Contains("fdaact4"))
            return Unknown("The captured dictionaries do not qualify the promotional submission/contact mapping.");
        var digest = target.InputDigest;
        cancellationToken.ThrowIfCancellationRequested();
        var inspections = lifecycle.XmlInspections.Where(xml => xml.InputDigest == digest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (inspections.Length != 1) return Unknown("A unique target XML inspection is required.");
        var xml = inspections[0];
        var prefix = target.Manifest.SequenceNumber + "/";
        var incomplete = !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code is "INVALID_REGIONAL_BACKBONE_REFERENCE" or "REGIONAL_PROFILE_REQUIRED");
        var documents = xml.Backbones.Where(document => document.Kind == BackboneKind.UsRegional).GroupBy(document => document.LogicalPath, StringComparer.Ordinal).ToArray();
        if (documents.Length == 0) incomplete = true;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        foreach (var group in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() != 1 || !group.Key.StartsWith(prefix, StringComparison.Ordinal)) { incomplete = true; continue; }
            var document = group.Single();
            if (!document.ReadComplete || document.DocumentTypeName != "fda-regional:fda-regional") { incomplete = true; continue; }
            var nodeGroups = document.Elements.GroupBy(element => element.NodePath, StringComparer.Ordinal).ToArray();
            if (nodeGroups.Any(nodes => nodes.Count() != 1)) { incomplete = true; continue; }
            var byPath = nodeGroups.ToDictionary(nodes => nodes.Key, nodes => nodes.Single(), StringComparer.Ordinal);
            var graphKnown = true;
            foreach (var element in document.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var path in element.Children)
                    if (!byPath.TryGetValue(path, out var child) || child.ParentPath != element.NodePath) graphKnown = false;
            }
            if (!graphKnown) { incomplete = true; continue; }
            var roots = document.Elements.Where(element => element.ParentPath is null).ToArray();
            if (roots.Length != 1 || roots[0].LocalName != "fda-regional" || roots[0].NamespaceUri != "http://www.ich.org/fda")
            { incomplete = true; continue; }
            ParsedXmlElement[] Children(ParsedXmlElement parent, string name) => parent.Children.Select(path => byPath[path])
                .Where(element => element.Name == name && element.LocalName == name && element.NamespaceUri.Length == 0).ToArray();
            var admins = Children(roots[0], "admin");
            if (admins.Length != 1) { incomplete = true; continue; }
            var admin = admins[0];
            var sets = Children(admin, "application-set");
            if (sets.Length != 1) { incomplete = true; continue; }
            var applications = Children(sets[0], "application");
            if (applications.Length == 0) { incomplete = true; continue; }
            var triggered = false;
            var triggerUnknown = false;
            foreach (var application in applications)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var information = Children(application, "submission-information");
                if (information.Length != 1) { triggerUnknown = true; continue; }
                var ids = Children(information[0], "submission-id");
                if (ids.Length != 1 || ids[0].Children.Count > 0) { triggerUnknown = true; continue; }
                var type = ExplicitAttribute(ids[0], "submission-type");
                if (type is null || !submissionTypes.Contains(type)) { triggerUnknown = true; continue; }
                if (type == "fdast8") triggered = true;
            }
            if (!triggered) { incomplete |= triggerUnknown; continue; }
            applicable = true;
            var applicants = Children(admin, "applicant-info");
            if (applicants.Length > 1) { incomplete = true; continue; }
            var location = applicants.SingleOrDefault() ?? admin;
            var containers = applicants.Length == 0 ? [] : Children(applicants[0], "applicant-contacts");
            if (containers.Length > 1) { incomplete = true; continue; }
            var contacts = containers.Length == 0 ? [] : Children(containers[0], "applicant-contact");
            var matched = false;
            var contactUnknown = false;
            foreach (var contact in contacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var names = Children(contact, "applicant-contact-name");
                if (names.Length == 0) continue;
                if (names.Length != 1 || names[0].Children.Count > 0) { contactUnknown = true; continue; }
                var type = ExplicitAttribute(names[0], "applicant-contact-type");
                if (type is null || !contactTypes.Contains(type)) { contactUnknown = true; continue; }
                if (type == "fdaact4" && !string.IsNullOrWhiteSpace(names[0].Text)) matched = true;
            }
            if (matched) continue;
            if (contactUnknown) { incomplete = true; continue; }
            if (remaining <= 0) { incomplete = true; continue; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The promotional submission omits a promotional labeling and advertising regulatory contact name.",
                location.Location with { FieldPath = "applicant-contacts", Files = [new(target.SourceId, document.LogicalPath)] },
                Expected: "Nonblank applicant-contact-name with applicant-contact-type fdaact4", Actual: "Not provided",
                RecommendedAction: "Provide the promotional labeling and advertising regulatory contact in future promotional submissions."));
            retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A required promotional contact is missing; remaining regional scope may be incomplete.", findings.ToArray()));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked promotional submission types and contact names within each target regional admin. Unknown codes and ambiguous or unread structures cannot prove absence.", findings);
    }

    private static string? ExplicitAttribute(ParsedXmlElement element, string name)
        => element.Attributes.GetValueOrDefault(name) is { IsDefault: false, NamespaceUri.Length: 0 } attribute && attribute.LocalName == name ? attribute.Value : null;
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
