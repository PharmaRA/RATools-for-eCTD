using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaIndexDtdVersionRule
{
    internal const string RuleId = "FDA47-1442";

    internal static RuleCheckResult? Inspect(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The index version declaration inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1442" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1442 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1442.");

        var digest = target.InputDigest;
        cancellationToken.ThrowIfCancellationRequested();
        var inspections = lifecycle.XmlInspections.Where(xml => xml.InputDigest == digest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (inspections.Length != 1) return Unknown("A unique target XML inspection is required.");
        var path = target.Manifest.SequenceNumber + "/index.xml";
        var documents = inspections[0].Backbones.Where(document => document.Kind == BackboneKind.Ich && document.LogicalPath == path).ToArray();
        if (documents.Length != 1 || documents[0].DocumentTypeName != "ectd:ectd")
            return Unknown("A unique identified target ICH index is required.");
        var roots = documents[0].Elements.Where(element => element.ParentPath is null).ToArray();
        if (roots.Length != 1 || roots[0].Name != "ectd:ectd" || roots[0].LocalName != "ectd" || roots[0].NamespaceUri != "http://www.ich.org/ectd")
            return Unknown("The complete attribute facts of the identified index root are unavailable or ambiguous.");

        // The reader stores a root only after all its attributes have been captured.
        // A later body/tail error does not erase this root-level presence evidence.
        var root = roots[0];
        var attribute = root.Attributes.GetValueOrDefault("dtd-version");
        if (attribute is not null && (attribute.LocalName != "dtd-version" || attribute.NamespaceUri.Length != 0))
            return Unknown("The version attribute identity is not established.");
        if (attribute is { IsDefault: false })
            return attribute.Value == "3.2" ? new(RuleId, CheckStatus.Pass, "The target index root explicitly supplies its DTD version.") :
                Unknown("The retained explicit version is not qualified by the selected index profile; it is not inferred to be omitted.");
        if (remaining <= 0) return Unknown("The shared finding budget is exhausted.");
        var result = new RuleCheckResult(RuleId, CheckStatus.Fail, "The target index root omits an explicit DTD version.",
            [new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "A DTD default does not replace an explicit dtd-version declaration in index.xml.",
                root.Location with { FieldPath = "@dtd-version", Files = [new(target.SourceId, path)] },
                Expected: "Explicit dtd-version", Actual: attribute is null ? "Absent" : "DTD default: " + attribute.Value,
                RecommendedAction: "Review the explicit DTD version declaration in the target index.xml.")]);
        retainProgress?.Invoke(result);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
