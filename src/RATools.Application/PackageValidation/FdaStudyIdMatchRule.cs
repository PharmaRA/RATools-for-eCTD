using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStudyIdMatchRule
{
    internal const string RuleId = "FDA47-1738";
    internal static async Task<RuleCheckResult?> InspectAsync(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The study identity inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1738" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1738 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2023, 3, 16))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1738.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StudyDataTechnicalGuide) ||
            !context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured study-data guide and STF specification are required.");

        var data = await PackageStudyDataInspector.InspectAsync(packages, lifecycle, context, cancellationToken: cancellationToken).ConfigureAwait(false);
        var incomplete = !data.ScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            foreach (var document in data.Documents)
            {
                Tick();
                var inScope = false;
                var qualified = true;
                foreach (var owner in document.Stf.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    if (!owner.Qualified || owner.Heading is null || !context.NodeSchema.Definitions.TryGetValue(owner.Heading, out var definition) ||
                        definition.SectionPath is not { } section) { qualified = false; continue; }
                    if (section.StartsWith("m4.", StringComparison.Ordinal) || section.StartsWith("m5.", StringComparison.Ordinal)) inScope = true;
                }
                if (!qualified) { incomplete = true; continue; }
                if (!inScope) continue;
                if (!document.Stf.StructureComplete) { incomplete = true; continue; }
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                foreach (var element in document.Stf.Document.Elements) { Tick(); if (!elements.TryAdd(element.NodePath, element)) qualified = false; }
                if (!qualified) { incomplete = true; continue; }
                var roots = elements.Values.Where(element => element.ParentPath is null && element.LocalName == "study" && element.NamespaceUri == "http://www.ich.org/ectd").ToArray();
                if (roots.Length != 1) { incomplete = true; continue; }
                var identifiers = Children(roots[0], "study-identifier");
                var ids = identifiers.Count == 1 ? Children(identifiers[0], "study-id") : [];
                if (ids.Count != 1 || ids[0].Children.Count != 0 || string.IsNullOrWhiteSpace(ids[0].Text)) { incomplete = true; continue; }
                var studyId = ids[0].Text;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reference in document.References)
                {
                    Tick();
                    if (reference.Reference.Status != StfReferenceStatus.Resolved || reference.ContentPath is not { } path)
                    { incomplete = true; continue; }
                    if (!string.Equals(path.Split('/')[^1], "ts.xpt", StringComparison.Ordinal)) continue;
                    if (!seen.Add(path)) continue;
                    applicable = true;
                    if (reference.Status != StudyDataReferenceStatus.Read || reference.TrialSummary is not { Status: TrialSummaryStatus.Read, StructureComplete: true } ts)
                    { incomplete = true; continue; }
                    var comparison = CompareCore(studyId, ts, Tick);
                    if (comparison == CheckStatus.NotEvaluated) { incomplete = true; continue; }
                    if (comparison == CheckStatus.Pass) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "The STF study-id has no exact match in STUDYID or an SPREFID parameter value of this referenced TS.",
                        ids[0].Location with { FieldPath = "study-identifier/study-id",
                            Files = [new(document.Stf.SourceId, document.Stf.Document.LogicalPath), new(ts.Capture.SourceId, path)] },
                        Expected: studyId, Actual: "No exact STUDYID or SPREFID match in " + path));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A study identity mismatch is established; other associated TS pairs may remain unqualified.", findings.ToArray()));
                }

                List<ParsedXmlElement> Children(ParsedXmlElement parent, string name)
                {
                    var result = new List<ParsedXmlElement>();
                    foreach (var childPath in parent.Children)
                    {
                        Tick();
                        if (elements.TryGetValue(childPath, out var child) && child.ParentPath == parent.NodePath && child.NamespaceUri.Length == 0 && child.LocalName == name)
                            result.Add(child);
                    }
                    return result;
                }
            }
        }
        catch (StudyIdentityWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Compared each actual target STF with each associated TS, including selected historical content. Exact STUDYID or SPREFID matches use definite rows; unresolved values and possible rows only block a mismatch when they could supply a match. Missing TS obligations are separate.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new StudyIdentityWorkLimitException(); }
    }

    internal static CheckStatus Compare(string studyId, PackageTrialSummaryFacts summary, CancellationToken cancellationToken)
        => CompareCore(studyId, summary, cancellationToken.ThrowIfCancellationRequested);

    private static CheckStatus CompareCore(string studyId, PackageTrialSummaryFacts summary, Action tick)
    {
        tick();
        if (string.IsNullOrWhiteSpace(studyId) || summary.Status != TrialSummaryStatus.Read || !summary.StructureComplete ||
            summary.MinPossibleRows is not { } minimum || summary.MaxPossibleRows is not { } maximum ||
            minimum < 0 || maximum < minimum || summary.Rows.Count != maximum) return CheckStatus.NotEvaluated;
        var couldMatch = false;
        foreach (var row in summary.Rows)
        {
            tick();
            var match = string.Equals(row.StudyId, studyId, StringComparison.Ordinal) ||
                row.Parameter == "SPREFID" && string.Equals(row.Value, studyId, StringComparison.Ordinal);
            if (row.Certain && match) return CheckStatus.Pass;
            // Null means the field could not be decoded, not an empty string. Unknown parameters
            // matter only if their value could match; optional null-flavor is irrelevant here.
            var possible = row.StudyId is null || (row.Parameter is null or "SPREFID") &&
                (row.Value is null || string.Equals(row.Value, studyId, StringComparison.Ordinal));
            if (possible || !row.Certain && match) couldMatch = true;
        }
        tick();
        return couldMatch ? CheckStatus.NotEvaluated : CheckStatus.Fail;
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class StudyIdentityWorkLimitException : Exception;
}
