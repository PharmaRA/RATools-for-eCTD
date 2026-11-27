using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaUtilityFileRule
{
    internal const string RuleId = "FDA47-1314";
    private static readonly ValidationRuleSource UsStylesheet = new("fda-us-stylesheet", RuleSourceKind.RegulatorySpecification, "2.2",
        "https://www.accessdata.fda.gov/static/eCTD/us-regional.zip", "ecfd18683a23db598ec2a8ee841f098bd27d47ed00c648dfedcbdd4a4aecc37e", "Verified");

    internal static IEnumerable<RuleCheckResult> Inspect(ICapturedPackageInput target, string profile, PackageStandardsContext context,
        int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(profile).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) yield break;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1314" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "FDA 1314 requires the archived criteria and a selection date.");
            yield break;
        }
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
        {
            yield return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 1314's effective date.");
            yield break;
        }
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.IchQa))
        {
            yield return new(RuleId, CheckStatus.NotEvaluated, "The archived ICH Q&A 51 is required to assess regional utility files.");
            yield break;
        }

        var accepted = context.Assets.Assets.Values.ToLookup(asset => asset.LogicalPath, asset => asset.Sha256, StringComparer.Ordinal);
        var prefix = target.Manifest.SequenceNumber + "/";
        var findings = new List<ValidationFinding>();
        var failures = 0;
        var incomplete = false;
        foreach (var file in target.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.LogicalPath.StartsWith(prefix + "util/", StringComparison.Ordinal)) continue;
            var relative = file.LogicalPath[prefix.Length..];
            if (accepted[relative].Contains(file.Sha256, StringComparer.Ordinal)) continue;
            if (relative == "util/style/us-regional.xsl" && file.Sha256 == "41fe92721f947fb3bb1dbe6209bccc515d09eb67adbdf3b12e22ad889e963599" &&
                context.HasArchivedOfficialSource(UsStylesheet)) continue;
            if (failures >= remaining) { incomplete = true; break; }
            var status = IsFinderMetadata(target, file, cancellationToken) ? CheckStatus.Fail : CheckStatus.NotEvaluated;
            if (status == CheckStatus.NotEvaluated)
            {
                incomplete = true;
                if (findings.Count >= remaining) continue;
            }
            else
            {
                if (findings.Count >= remaining) findings.RemoveAt(findings.FindLastIndex(item => item.CheckStatus == CheckStatus.NotEvaluated));
                failures++;
            }
            findings.Add(new(RuleId, status, rule.Severity, RuleId, status == CheckStatus.Fail ? "Finder metadata was delivered in the utility directory." :
                "The captured standards do not establish this file's utility role.", new(target.Manifest.SequenceNumber, file.LogicalPath, Files: [new(target.SourceId, file.LogicalPath)]),
                Actual: file.LogicalPath, RecommendedAction: "Review the file against ICH Q&A 51 and the applicable regional utility requirements."));
        }
        yield return new(RuleId, failures > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Assessed files delivered under the target sequence's util directory using archived utility assets and ICH Q&A 51.", findings);
    }

    private static bool IsFinderMetadata(ICapturedPackageInput target, PackageInputFile file, CancellationToken token)
    {
        if (!file.LogicalPath.EndsWith("/.DS_Store", StringComparison.Ordinal) || file.Length < 8) return false;
        try
        {
            token.ThrowIfCancellationRequested();
            using var stream = target.OpenRead(file.LogicalPath);
            Span<byte> header = stackalloc byte[8];
            var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            token.ThrowIfCancellationRequested();
            return length == header.Length && header.SequenceEqual("\0\0\0\u0001Bud1"u8);
        }
        catch (Exception exception) when (exception is IOException and not PackageInputException or UnauthorizedAccessException)
        {
            // Unavailable metadata remains unknown. Input integrity and cancellation
            // still propagate; unrelated known utility failures must not be discarded.
            token.ThrowIfCancellationRequested();
            return false;
        }
    }
}
