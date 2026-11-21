using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfFileExtensionRule
{
    internal const string RuleId = "FDA47-1867";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF extension inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1867" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1867 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1867.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured STF specification is required.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var knownCurrentPaths = new HashSet<string>(StringComparer.Ordinal);
        var work = packages.Target.Limits.MaxXmlNodes;
        var targetNumber = packages.Target.Manifest.SequenceNumber;
        try
        {
            foreach (var bound in bindings.Documents)
            {
                Tick();
                var selected = bound.IsTarget;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != targetNumber) continue;
                    if (!owner.Qualified) { if (!bound.IsTarget) incomplete = true; continue; }
                    selected = true;
                }
                if (!selected) continue;
                var document = bound.Document;
                var observations = 0;
                foreach (var xml in lifecycle.XmlInspections)
                {
                    Tick();
                    if (xml.InputDigest != bound.InputDigest) continue;
                    foreach (var observed in xml.StudyTaggingFiles.Documents)
                    { Tick(); if (observed.LogicalPath == document.LogicalPath) observations++; }
                }
                if (observations != 1) { incomplete = true; continue; }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                // A DOCTYPE-only candidate or contradictory root is not a confirmed STF.
                if (roots.Count != 1 || roots[0] is not { Name: "ectd:study", LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" })
                { incomplete = true; continue; }
                if (bound.IsTarget) knownCurrentPaths.Add(document.LogicalPath);
                applicable = true;
                incomplete |= !document.ReadComplete || document.DtdStatus != CheckStatus.Pass;
                if (document.LogicalPath.EndsWith(".xml", StringComparison.Ordinal)) continue;
                if (remaining <= 0) { incomplete = true; continue; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "An identified STF does not use the specified .xml file extension.",
                    roots[0].Location with { FieldPath = "file-extension", Files = [new(bound.SourceId, document.LogicalPath)] },
                    Expected: ".xml", Actual: Path.GetExtension(document.LogicalPath)));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "An STF extension violation is established; other identities may remain unknown.", findings.ToArray()));
            }
            // Missing content cannot prove its own role. This is local to 1867;
            // do not change the shared binding scope for other STF rules.
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                if (xml.InputDigest != packages.Target.InputDigest || xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId ||
                    xml.RulesDigest != context.Catalog.Digest || xml.AssetManifestDigest != context.Assets.Digest || xml.LimitsDigest != packages.Target.Limits.Digest()) continue;
                foreach (var backbone in xml.Backbones)
                {
                    Tick();
                    if (backbone.Kind != BackboneKind.Ich || backbone.LogicalPath != targetNumber + "/index.xml") continue;
                    foreach (var element in backbone.Elements)
                    {
                        Tick();
                        if (element.Name != "leaf" || element.LocalName != "leaf" || element.NamespaceUri.Length != 0) continue;
                        var operation = Explicit(element, "operation", "");
                        if (operation == "delete") continue;
                        PackageReference reference;
                        try { reference = PackageLogicalPath.ResolveReference(backbone.LogicalPath, Explicit(element, "href", "http://www.w3c.org/1999/xlink")!); }
                        catch (ArgumentException) { incomplete = true; continue; }
                        if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null)
                        { incomplete = true; continue; }
                        if (knownCurrentPaths.Contains(path)) continue;
                        if (operation is not ("new" or "append" or "replace")) { incomplete = true; continue; }
                        var sequence = path.Split('/')[0];
                        if (!packages.Sequences.TryGetValue(sequence, out var capture)) { incomplete = true; continue; }
                        var files = new List<PackageInputFile>();
                        foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files.Add(file); }
                        if (files.Count != 1) { incomplete = true; continue; }
                        if (sequence == targetNumber) continue; // Target discovery already participates in TargetScopeComplete.
                        var selectedContext = context.ForSequence(sequence);
                        string? profile = null;
                        foreach (var entry in packages.Baseline.Entries) { Tick(); if (entry.SequenceNumber == sequence) profile = entry.ProfileSnapshotId; }
                        var observations = new List<PackageXmlInspection>();
                        foreach (var historical in lifecycle.XmlInspections)
                        {
                            Tick();
                            if (historical.InputDigest == capture.InputDigest && historical.ProfileSnapshotId == profile &&
                                historical.RulesDigest == selectedContext.Catalog.Digest && historical.AssetManifestDigest == selectedContext.Assets.Digest &&
                                historical.LimitsDigest == capture.Limits.Digest()) observations.Add(historical);
                        }
                        if (observations.Count != 1) { incomplete = true; continue; }
                        var observed = new List<ParsedStudyTaggingFile>();
                        foreach (var document in observations[0].StudyTaggingFiles.Documents)
                        { Tick(); if (document.LogicalPath == path) observed.Add(document); }
                        if (observed.Count == 0)
                        { incomplete |= !observations[0].StudyTaggingFiles.InventoryComplete; continue; }
                        if (observed.Count != 1 || observed[0].CapturedSha256 != files[0].Sha256) { incomplete = true; continue; }
                        var qualified = false;
                        foreach (var bound in bindings.Documents)
                        {
                            Tick();
                            if (bound.InputDigest != capture.InputDigest || bound.Document.LogicalPath != path) continue;
                            foreach (var owner in bound.Owners)
                            {
                                Tick();
                                if (owner.BackboneLogicalPath == backbone.LogicalPath && owner.Leaf.NodePath == element.NodePath && owner.Qualified) qualified = true;
                            }
                        }
                        if (!qualified) incomplete = true;
                    }
                }
            }
        }
        catch (ExtensionWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked exact .xml extensions on identified target STFs and qualified current-owner references to historical STFs. Filename prefixes and study-id naming are separate; incomplete identity cannot pass.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new ExtensionWorkLimitException(); }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            string? value = null;
            var count = 0;
            foreach (var attribute in element.Attributes.Values)
            {
                Tick();
                if (attribute.LocalName != name || attribute.NamespaceUri != ns || attribute.IsDefault) continue;
                count++; value = attribute.Value;
            }
            return count == 1 ? value : null;
        }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class ExtensionWorkLimitException : Exception;
}
