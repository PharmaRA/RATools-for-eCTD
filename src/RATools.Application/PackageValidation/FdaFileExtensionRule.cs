using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaFileExtensionRule
{
    internal const string RuleId = "FDA47-1255";
    private static readonly ValidationRuleSource FormatSource = new("fda-file-formats", RuleSourceKind.RegulatorySpecification,
        "9.3", "https://www.fda.gov/media/85816/download?attachment", "8d5b4f2ef220c16324d83e44cc50595e087ccd44d1db42ebcca4084976abae6b", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1255" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.VerificationStatus != "Verified" || source.Version != "4.7" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The criteria source and selection date are required.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of criterion 1255.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2025, 8, 20) || !context.HasArchivedOfficialSource(FormatSource))
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "This check requires the archived file-format revision 9.3 and a selection on or after its revision date.");
            yield break;
        }
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var index = xml?.Backbones.SingleOrDefault(item => item.LogicalPath == target.Manifest.SequenceNumber + "/index.xml");
        if (index is null || !index.ReadComplete || index.DtdStatus != CheckStatus.Pass)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "Module assignment requires a fully read, DTD-valid ICH backbone.");
            yield break;
        }
        var findings = new List<ValidationFinding>();
        var files = target.Manifest.Files.Select(file => file.LogicalPath).ToHashSet(StringComparer.Ordinal);
        var modules = new Dictionary<string, int>(StringComparer.Ordinal);
        var incomplete = false;
        foreach (var element in index.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parentModule = element.ParentPath is null ? 0 : modules[element.ParentPath];
            modules[element.NodePath] = element.LocalName switch
            {
                "m2-common-technical-document-summaries" => 2,
                "m3-quality" => 3,
                "m4-nonclinical-study-reports" => 4,
                "m5-clinical-study-reports" => 5,
                _ => parentModule
            };
        }
        foreach (var leaf in index.Leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (leaf.IsRegionalReference || leaf.Operation == "delete") continue;
            var module = modules[leaf.NodePath];
            if (module == 0 || leaf.Operation is not ("new" or "replace" or "append") || string.IsNullOrWhiteSpace(leaf.Href))
            { incomplete = true; continue; }
            PackageReference reference;
            try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href); }
            catch (ArgumentException) { incomplete = true; continue; }
            if (reference.LogicalPath is not { } path || reference.Query is not null || !files.Contains(path))
            { incomplete = true; continue; }
            bool? allowed = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".pdf" or ".jpg" or ".jpeg" or ".png" => true,
                ".txt" or ".xls" or ".xlsx" or ".xpt" or ".r" or ".sas" or ".svg" => module >= 3,
                ".rmd" or ".json" or ".cas" or ".dat" => module == 5,
                ".sdf" => module == 3,
                _ => null
            };
            if (allowed == true) continue;
            if (allowed is null || remaining <= 0) { incomplete = true; continue; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, $"The extension is outside the accepted eCTD modules in FDA format revision 9.3 (M{module}).",
                leaf.Location with { FieldPath = "@xlink:href", Files = [new(target.SourceId, index.LogicalPath), new(target.SourceId, path)] }, Actual: leaf.Href,
                RecommendedAction: "Review the referenced file and its placement in the eCTD backbone."));
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked selected format rows for target ICH module 2 through 5 content leaves. Regional and remaining file-format rows require separate assessment.", findings);
    }
}
