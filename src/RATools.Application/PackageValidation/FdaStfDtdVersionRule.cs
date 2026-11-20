using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfDtdVersionRule
{
    internal const string RuleId = "FDA47-1752";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF root version inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1752" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1752 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1752.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification) ||
            !context.Assets.Assets.TryGetValue("stf-dtd", out var asset) || asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f")
            return Unknown("The captured STF specification and pinned STF DTD are required.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            foreach (var bound in bindings.Documents)
            {
                Tick();
                // The target delivers STF XML independently of whether a leaf references it.
                // Older files are selected only through an actual current owner, never because
                // their sequence happens to be included in the historical baseline.
                var selected = bound.IsTarget;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    if (!owner.Qualified) { incomplete = true; continue; }
                    selected = true;
                }
                if (!selected) continue;
                applicable = true;
                var document = bound.Document;
                if (!seen.Add(document.LogicalPath)) { incomplete = true; continue; }
                // Binding discovery retains the first duplicate path for diagnostics. A retained
                // object is not proof that its own root evidence is unambiguous: reject all
                // duplicates at that path before allowing a local omission to become a finding.
                var observations = 0;
                foreach (var xml in lifecycle.XmlInspections)
                {
                    Tick();
                    if (xml.InputDigest != bound.InputDigest) continue;
                    foreach (var observed in xml.StudyTaggingFiles.Documents)
                    {
                        Tick();
                        if (observed.LogicalPath == document.LogicalPath) observations++;
                    }
                }
                if (observations != 1) { incomplete = true; continue; }
                incomplete |= !document.ReadComplete || document.DtdStatus != CheckStatus.Pass;
                var roots = new List<ParsedXmlElement>();
                foreach (var element in document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                if (roots.Count != 1 || roots[0] is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                { incomplete = true; continue; }
                var root = roots[0];
                ParsedXmlAttributeValue? version = null;
                foreach (var attribute in root.Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName == "dtd-version" && attribute.NamespaceUri.Length == 0) version = attribute;
                }
                // The reader retains the root only after the full attribute collection is read.
                // #FIXED supplies the effective CDATA value, not an explicit XML declaration.
                if (version is not null && (version.Name != "dtd-version" || version.Value != "2.2"))
                { incomplete = true; continue; }
                if (version is { IsDefault: false }) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "The STF root omits an explicit dtd-version declaration; a DTD default does not replace it.",
                    root.Location with { FieldPath = "@dtd-version", Files = [new(bound.SourceId, document.LogicalPath)] },
                    Expected: "Explicit dtd-version", Actual: version is null ? "Absent" : "DTD default: " + version.Value));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An STF root version omission is established; other files may remain unqualified.", findings.ToArray()));
            }
        }
        catch (StfVersionWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked explicit root dtd-version on actual target STF files, including unreferenced files, and qualified current-owner references to selected historical STF content. Incomplete discovery cannot pass; owner version presence and version agreement remain separate rules.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new StfVersionWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class StfVersionWorkLimitException : Exception;
}
