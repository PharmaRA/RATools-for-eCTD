using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaDuplicateRegionalRule
{
    internal const string RuleId = "FDA47-1111";

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1111" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1111 requires the archived criteria and a selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2013, 4, 30))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 1111's effective date.");
            yield break;
        }

        var findings = new List<ValidationFinding>();
        string? firstFile = null;
        var incomplete = false;
        foreach (var file in target.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.LogicalPath.EndsWith("/us-regional.xml", StringComparison.Ordinal)) continue;
            if (firstFile is null) { firstFile = file.LogicalPath; continue; }
            if (findings.Count >= remaining) { incomplete = true; break; }
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, "DUPLICATE_US_REGIONAL_FILE", "The target sequence contains multiple us-regional.xml files.",
                new(target.Manifest.SequenceNumber, file.LogicalPath, Files: [new(target.SourceId, firstFile), new(target.SourceId, file.LogicalPath)]),
                RecommendedAction: "Review the listed files and submit one US regional backbone."));
        }
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var index = xml?.Backbones.SingleOrDefault(item => item.Kind == BackboneKind.Ich && item.LogicalPath == target.Manifest.SequenceNumber + "/index.xml");
        if (index is null || !index.ReadComplete) incomplete = true;
        else
        {
            var root = index.Elements[0];
            var modulePaths = index.Elements.Where(element => element.ParentPath == root.NodePath && element.NamespaceUri.Length == 0 &&
                element.LocalName == "m1-administrative-information-and-prescribing-information").Select(element => element.NodePath).ToHashSet(StringComparer.Ordinal);
            var regionalPaths = xml!.Backbones.Where(document => document.Kind == BackboneKind.UsRegional && document.ReadComplete)
                .Select(document => document.LogicalPath).ToHashSet(StringComparer.Ordinal);
            var foundReference = false;
            foreach (var leaf in index.Leaves.Where(item => item.IsRegionalReference))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (leaf.Operation == "delete") continue;
                if (leaf.ParentNodePath is null || !modulePaths.Contains(leaf.ParentNodePath)) { incomplete = true; continue; }
                if (leaf.Operation is not ("new" or "replace" or "append") || string.IsNullOrWhiteSpace(leaf.Href)) { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null ||
                    !path.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal) || path == index.LogicalPath ||
                    path != target.Manifest.SequenceNumber + "/m1/us/us-regional.xml" && !regionalPaths.Contains(path))
                { incomplete = true; continue; }
                if (!foundReference) { foundReference = true; continue; }
                if (findings.Count >= remaining) { incomplete = true; break; }
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, "DUPLICATE_US_REGIONAL_REFERENCE", "The target M1 contains multiple regional backbone references.",
                    leaf.Location with { FieldPath = "@xlink:href", Files = [new(target.SourceId, index.LogicalPath)] }, Actual: leaf.Href,
                    RecommendedAction: "Review the M1 leaf references and submit one reference to the regional backbone."));
            }
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            incomplete ? "The target ICH backbone, regional reference syntax or finding budget prevented a complete comparison." :
                "Counted US regional files and M1 references in the target sequence.", findings);
    }
}
