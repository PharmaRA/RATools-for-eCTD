using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaIndexChecksumFormatRule
{
    internal const string RuleId = "FDA47-1391";

    internal static async Task<RuleCheckResult?> InspectAsync(ICapturedPackageInput target, string profile,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken)
    {
        var rule = context.Catalog.ForProfile(profile).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1391" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return new(RuleId, CheckStatus.NotEvaluated, "FDA 1391 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1391.");
        var path = target.Manifest.SequenceNumber + "/index-md5.txt";
        var file = target.Manifest.Files.SingleOrDefault(item => item.LogicalPath == path);
        if (file is null) return new(RuleId, CheckStatus.NotEvaluated, "The target index-md5.txt is not available for format inspection.");
        var valid = false;
        if (file.Length == 32)
        {
            try
            {
                using var stream = target.OpenRead(path);
                var bytes = new byte[32];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                var extra = new byte[1];
                if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
                    return new(RuleId, CheckStatus.NotEvaluated, "The checksum stream differs from its captured length.");
                valid = bytes.All(value => value is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F');
            }
            catch (IOException) { return new(RuleId, CheckStatus.NotEvaluated, "The checksum bytes could not be completely read."); }
        }
        if (valid) return new(RuleId, CheckStatus.Pass, "The target index-md5.txt contains exactly 32 ASCII hexadecimal bytes; checksum matching is assessed separately.");
        if (remaining <= 0) return new(RuleId, CheckStatus.NotEvaluated, "The shared finding budget is exhausted.");
        return new(RuleId, CheckStatus.Fail, "The target index-md5.txt has an invalid MD5 representation.",
            [new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "index-md5.txt must contain exactly 32 ASCII hexadecimal bytes without extra whitespace or encoding markers.",
                new(target.Manifest.SequenceNumber, path, Files: [new(target.SourceId, path)]),
                Expected: "32 ASCII hexadecimal bytes", Actual: file.Length == 32 ? "Contains non-hexadecimal bytes" : file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes",
                RecommendedAction: "Review the checksum text format and the submission procedure.")]);
    }
}
