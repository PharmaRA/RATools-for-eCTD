using System.Globalization;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaTrialSummaryRequirementRule
{
    internal const string RuleId = "FDA47-1734";
    private static readonly string[] Sections = ["m4.2.3.1", "m4.2.3.2", "m4.2.3.4", "m5.3.1.1", "m5.3.1.2", "m5.3.3.1",
        "m5.3.3.2", "m5.3.3.3", "m5.3.3.4", "m5.3.4", "m5.3.5.1", "m5.3.5.2"];
    internal static async Task<RuleCheckResult?> InspectAsync(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The trial summary requirement inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1734" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1734 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2021, 9, 15))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes this FDA 1734 version's effective date.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StudyDataTechnicalGuide) ||
            !context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) || context.FdaM1Policy is null)
            return Unknown("Captured study-data, STF and M1 application-type sources are required.");
        var data = await PackageStudyDataInspector.InspectAsync(packages, lifecycle, context, cancellationToken: cancellationToken).ConfigureAwait(false);
        var incomplete = !data.ScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var applicationType = ApplicationType();
            foreach (var document in data.Documents)
            {
                Tick();
                var owners = document.Stf.Owners.Where(owner => owner.Leaf.Address?.SequenceNumber == packages.Target.Manifest.SequenceNumber).ToArray();
                var scopes = new HashSet<bool>();
                var unknownScope = false;
                foreach (var owner in owners)
                {
                    Tick();
                    if (!owner.Qualified || owner.Heading is null || !context.NodeSchema.Definitions.TryGetValue(owner.Heading, out var definition) ||
                        definition.SectionPath is not { } section) { unknownScope = true; continue; }
                    if (Sections.Any(prefix => section == prefix || section.StartsWith(prefix + ".", StringComparison.Ordinal)))
                        scopes.Add(section.StartsWith("m5", StringComparison.Ordinal));
                }
                if (unknownScope) incomplete = true;
                if (scopes.Count == 0) continue;
                if (scopes.Count != 1 || unknownScope || !document.Stf.StructureComplete) { incomplete = true; continue; }
                var clinical = scopes.Single();
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                foreach (var element in document.Stf.Document.Elements) { Tick(); if (!elements.TryAdd(element.NodePath, element)) unknownScope = true; }
                if (unknownScope) { incomplete = true; continue; }
                var triggered = false;
                var unknownTrigger = false;
                var unknownSupply = false;
                var summaries = new List<StudyDataReference>();
                foreach (var reference in document.References)
                {
                    Tick();
                    if (reference.Reference.Status != StfReferenceStatus.Resolved || reference.Reference.Leaf?.Address is not { } address || reference.ContentPath is null)
                    { unknownTrigger = true; unknownSupply = true; continue; }
                    var currentLeaf = address.SequenceNumber == packages.Target.Manifest.SequenceNumber;
                    var isTsName = string.Equals(reference.ContentPath.Split('/')[^1], "ts.xpt", StringComparison.Ordinal);
                    if (isTsName)
                    {
                        if (!currentLeaf) unknownSupply = true;
                        else summaries.Add(reference);
                        continue; // A TS does not itself create the obligation to provide a TS.
                    }
                    if (reference.Status == StudyDataReferenceStatus.Read)
                    {
                        if (reference.Xpt?.Content is { Status: XptReadStatus.Read, StructureComplete: true })
                        { if (currentLeaf) triggered = true; else unknownTrigger = true; }
                        else unknownTrigger = true;
                    }
                    else if (reference.Status != StudyDataReferenceStatus.NotSelected) unknownTrigger = true;
                    if (!clinical && HasReportTag(reference.Reference.Element, elements))
                    {
                        var number = reference.ContentPath.Split('/')[0];
                        var exists = false;
                        if (packages.Sequences.TryGetValue(number, out var capture))
                            foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == reference.ContentPath) exists = true; }
                        if (currentLeaf && exists) triggered = true;
                        else unknownTrigger = true;
                    }
                }
                if (!triggered) { if (unknownTrigger) incomplete = true; continue; }
                if (applicationType is not ("fdaat1" or "fdaat2" or "fdaat3") ||
                    !clinical && context.SelectionDate < new DateOnly(2023, 3, 16))
                { incomplete = true; continue; }
                applicable = true;
                var supplied = false;
                var definiteDefect = false;
                foreach (var summary in summaries)
                {
                    Tick();
                    if (summary.Status == StudyDataReferenceStatus.MissingContent) { definiteDefect = true; continue; }
                    if (summary.Status != StudyDataReferenceStatus.Read || summary.TrialSummary is not { } ts ||
                        summary.Xpt?.Content is not { Status: XptReadStatus.Read, StructureComplete: true })
                    { unknownSupply = true; continue; }
                    if (ts.Status == TrialSummaryStatus.NotTrialSummary) { definiteDefect = true; continue; }
                    if (ts.Status != TrialSummaryStatus.Read || !ts.StructureComplete) { unknownSupply = true; continue; }
                    var parameter = clinical ? "SSTDTC" : "STSTDTC";
                    var values = new HashSet<string>(StringComparer.Ordinal);
                    var unknownRows = false;
                    var parameterFound = false;
                    foreach (var row in ts.Rows)
                    {
                        Tick();
                        if (!row.Certain)
                        {
                            // A possible all-blank padding row cannot retract a definite SSD.
                            if (!row.ValuesDecoded || !string.IsNullOrEmpty(row.StudyId) || !string.IsNullOrEmpty(row.Parameter) ||
                                !string.IsNullOrEmpty(row.Value) || !string.IsNullOrEmpty(row.NullFlavor)) unknownRows = true;
                            continue;
                        }
                        if (row.Parameter is null) { unknownRows = true; continue; }
                        if (row.Parameter != parameter) continue;
                        parameterFound = true;
                        if (!row.ValuesDecoded || row.Value is null) { unknownRows = true; continue; }
                        if (row.Value.Length == 0 && string.IsNullOrEmpty(row.NullFlavor)) continue;
                        if (!string.IsNullOrEmpty(row.NullFlavor) || !Date(row.Value)) { unknownRows = true; continue; }
                        values.Add(row.Value);
                    }
                    if (values.Count == 1 && !unknownRows) supplied = true;
                    else if (values.Count > 1 || unknownRows) unknownSupply = true;
                    else if (!parameterFound || values.Count == 0) definiteDefect = true;
                }
                if (supplied && !unknownSupply) continue;
                if (unknownSupply) { incomplete = true; continue; }
                if (summaries.Count == 0 || definiteDefect)
                {
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    var root = document.Stf.Document.Elements.FirstOrDefault(element => element.ParentPath is null);
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        summaries.Count == 0 ? "This applicable study does not reference a dataset named ts.xpt." :
                            "The referenced ts.xpt does not supply the required trial-summary start-date information.",
                        (root?.Location ?? new ValidationLocation(LogicalPath: document.Stf.Document.LogicalPath)) with
                        { FieldPath = "study-document/ts.xpt", Files = [new(document.Stf.SourceId, document.Stf.Document.LogicalPath)] },
                        Expected: clinical ? "Associated ts.xpt with SSTDTC information" : "Associated ts.xpt with STSTDTC information"));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A missing trial-summary requirement is established; other scope may remain unqualified.", findings.ToArray()));
                }
            }
        }
        catch (TrialSummaryWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Assessed current study TS supply in the known NDA/ANDA/BLA and common center-date branches. Center-specific transition dates, IND commercial status, NA applicability and historical TS supply remain unqualified; this rule remains Partial.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new TrialSummaryWorkLimitException(); }
        string? ApplicationType()
        {
            var xml = lifecycle.XmlInspections.Where(xml => xml.InputDigest == packages.Target.InputDigest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
            if (xml.Length != 1) return null;
            var regions = xml[0].Backbones.Where(backbone => backbone.Kind == BackboneKind.UsRegional).ToArray();
            if (regions.Length != 1 || !regions[0].ReadComplete || regions[0].DtdStatus != CheckStatus.Pass) return null;
            var regional = regions[0];
            if (regional.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                !regional.LogicalPath.StartsWith(packages.Target.Manifest.SequenceNumber + "/", StringComparison.Ordinal)) return null;
            var captured = 0;
            foreach (var file in packages.Target.Manifest.Files) { Tick(); if (file.LogicalPath == regional.LogicalPath) captured++; }
            if (captured != 1) return null;
            var map = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in regional.Elements)
            {
                Tick();
                if (!map.TryAdd(element.NodePath, element) || element.Location.LogicalPath != regional.LogicalPath ||
                    element.Location.NodePath != element.NodePath) return null;
            }
            var roots = regional.Elements.Where(element => element.ParentPath is null).ToArray();
            if (roots.Length != 1 || roots[0].LocalName != "fda-regional" || roots[0].NamespaceUri != "http://www.ich.org/fda") return null;
            var applications = Child(roots[0], "admin").SelectMany(admin => Child(admin, "application-set")).SelectMany(set => Child(set, "application")).ToArray();
            var owners = new List<ParsedXmlElement>();
            foreach (var app in applications)
            {
                Tick();
                var attribute = app.Attributes.GetValueOrDefault("application-containing-files");
                if (attribute is not { NamespaceUri.Length: 0, Value: "true" or "false" }) return null;
                if (attribute.Value == "true") { if (attribute.IsDefault) return null; owners.Add(app); }
            }
            if (owners.Count != 1) return null;
            var numbers = Child(owners[0], "application-information").SelectMany(info => Child(info, "application-number")).ToArray();
            if (numbers.Length != 1 || numbers[0].Children.Count != 0) return null;
            var type = numbers[0].Attributes.GetValueOrDefault("application-type");
            return type is { IsDefault: false, NamespaceUri.Length: 0 } && context.FdaM1Policy.InputValues("application-type").Contains(type.Value, StringComparer.Ordinal) ? type.Value : null;

            IEnumerable<ParsedXmlElement> Child(ParsedXmlElement parent, string name)
            {
                foreach (var path in parent.Children)
                { Tick(); if (map.TryGetValue(path, out var child) && child.ParentPath == parent.NodePath && child.LocalName == name && child.NamespaceUri.Length == 0) yield return child; }
            }
        }
        bool HasReportTag(ParsedXmlElement element, IReadOnlyDictionary<string, ParsedXmlElement> elements)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (element.NamespaceUri.Length == 0 && element.LocalName is "doc-content" or "content-block")
            {
                Tick(); if (!seen.Add(element.NodePath)) return false;
                foreach (var path in element.Children)
                {
                    Tick();
                    if (elements.TryGetValue(path, out var child) && child.LocalName == "file-tag" && child.NamespaceUri.Length == 0 &&
                        child.Attribute("info-type", "") == "ich" && child.Attribute("name", "") is
                            "pre-clinical-study-report" or "legacy-clinical-study-report" or "study-report-body") return true;
                }
                if (element.ParentPath is not { } parent || !elements.TryGetValue(parent, out element!)) break;
            }
            return false;
        }
    }
    private static bool Date(string value) => value.Length == 10 && value[4] == '-' && value[7] == '-' &&
        value.Where((_, index) => index is not (4 or 7)).All(character => character is >= '0' and <= '9') &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class TrialSummaryWorkLimitException : Exception;
}
