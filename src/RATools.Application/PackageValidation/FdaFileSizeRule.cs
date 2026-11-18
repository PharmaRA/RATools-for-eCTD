using System.Globalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaFileSizeRule
{
    internal const string RuleId = "FDA47-1238";
    private const long DecimalLimit = 400_000_000;
    private const long BinaryLimit = 400L * 1024 * 1024;

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, string profile,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(profile).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1238" || rule.RuleVersion != "4.7.1" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Id != "fda-validation-criteria-4.7" ||
            source.Kind != RuleSourceKind.RegulatorySpecification || source.Version != "4.7" || source.VerificationStatus != "Verified" ||
            source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" || !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1238 requires the archived FDA criteria and an explicit selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selection date precedes FDA 1238's effective date.");
            yield break;
        }

        var findings = new List<ValidationFinding>();
        var incomplete = false;
        foreach (var file in target.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Length <= DecimalLimit) continue;
            if (file.Length <= BinaryLimit || remaining <= 0 || !IsPdfDocument(target, file.LogicalPath))
            {
                incomplete = true;
                continue;
            }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The captured PDF exceeds 400 MB under both byte conventions.",
                new(target.Manifest.SequenceNumber, file.LogicalPath, Files: [new(target.SourceId, file.LogicalPath)]),
                Expected: "400 MB maximum", Actual: file.Length.ToString(CultureInfo.InvariantCulture) + " bytes",
                RecommendedAction: "Review the document size against FDA 4.7 criterion 1238."));
        }
        yield return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            incomplete ? "Some target files require a byte-boundary decision, dataset or file-type assessment, or additional finding capacity." :
                "Compared captured target file lengths with the general FDA size limit.", findings);
    }

    private static bool IsPdfDocument(ICapturedPackageInput target, string path)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        using var stream = target.OpenRead(path);
        Span<byte> header = stackalloc byte[5];
        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length && header.SequenceEqual("%PDF-"u8);
    }
}
