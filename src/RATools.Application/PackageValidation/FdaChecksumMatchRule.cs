using System.Text;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaChecksumMatchRule
{
    internal const string RuleId = "FDA47-1374";
    private static readonly ValidationRuleSource IchSpecification = new("ich-specification", RuleSourceKind.RegulatorySpecification, "3.2.2",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf",
        "d8273833eac401ff3c094d782863b078110e33a7f358438d3f7a0dad277fc2b0", "Verified");

    internal static async Task<RuleCheckResult?> InspectAsync(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainKnownFailure = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1374" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return new(RuleId, CheckStatus.NotEvaluated, "FDA 1374 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selection precedes the effective date of FDA 1374.");
        if (!context.HasArchivedOfficialSource(IchSpecification))
            return new(RuleId, CheckStatus.NotEvaluated, "The selected snapshot requires the ICH checksum specification.");

        var target = packages.Target;
        var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
        var incomplete = xml is null || xml.Backbones.Count == 0 ||
            !xml.Checks.Any(check => check.RuleId == "XML-OFFLINE" && check.Status == CheckStatus.Pass) ||
            !xml.Checks.Any(check => check.RuleId == "XML-LIMITS" && check.Status == CheckStatus.Pass) ||
            xml.Checks.SelectMany(check => check.Findings).Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        var files = packages.Sequences.Values.SelectMany(capture => capture.Manifest.Files).ToDictionary(file => file.LogicalPath, StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();
        foreach (var document in xml?.Backbones ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.ReadComplete) { incomplete = true; continue; }
            var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var element = elements[leaf.NodePath];
                var operation = element.Attributes.GetValueOrDefault("operation");
                if (operation is not { IsDefault: false, Value: "new" or "append" or "replace" or "delete" }) { incomplete = true; continue; }
                if (operation.Value == "delete") continue;
                var checksum = element.Attributes.GetValueOrDefault("checksum");
                var type = element.Attributes.GetValueOrDefault("checksum-type");
                if (checksum is null || checksum.IsDefault || !IsMd5(checksum.Value) || type is not { IsDefault: false, Value: "md5" } || string.IsNullOrWhiteSpace(leaf.Href))
                { incomplete = true; continue; }
                PackageReference reference;
                try { reference = PackageLogicalPath.ResolveReference(document.LogicalPath, leaf.Href); }
                catch (ArgumentException) { incomplete = true; continue; }
                if (reference.LogicalPath is not { } path || reference.Query is not null ||
                    string.CompareOrdinal(path.Split('/')[0], target.Manifest.SequenceNumber) > 0 || !files.TryGetValue(path, out var file) || !IsMd5(file.Md5))
                { incomplete = true; continue; }
                Compare(checksum.Value, file.Md5, leaf.Location with { FieldPath = "@checksum", Line = checksum.Line, Column = checksum.Column,
                    Files = [new(target.SourceId, document.LogicalPath)] });
            }
        }

        var indexPath = target.Manifest.SequenceNumber + "/index.xml";
        var checksumPath = target.Manifest.SequenceNumber + "/index-md5.txt";
        if (!files.TryGetValue(indexPath, out var index) || !IsMd5(index.Md5) ||
            !files.TryGetValue(checksumPath, out var indexChecksum) || indexChecksum.Length != 32)
            incomplete = true;
        else
        {
            try
            {
                using var stream = target.OpenRead(checksumPath);
                var bytes = new byte[32];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                var extra = new byte[1];
                var value = Encoding.ASCII.GetString(bytes);
                if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0 || !IsMd5(value)) incomplete = true;
                else Compare(value, index.Md5, new(target.Manifest.SequenceNumber, checksumPath, Files: [new(target.SourceId, checksumPath)]));
            }
            catch (IOException) { incomplete = true; }
        }
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Compared valid MD5 declarations with captured bytes for target backbone leaves and index.xml. Missing comparison evidence and unsupported XML scopes remain unevaluated.", findings);

        void Compare(string declared, string actual, ValidationLocation location)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase)) return;
            if (remaining <= 0) { incomplete = true; return; }
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId, "The declared MD5 differs from the captured file bytes.", location,
                Expected: actual, Actual: declared, RecommendedAction: "Review the checksum and the identity of the supplied file to determine the cause of the mismatch."));
            retainKnownFailure?.Invoke(new(RuleId, CheckStatus.Fail, "A mismatch was established; remaining checksum comparisons may be incomplete.", findings.ToArray()));
        }
    }

    private static bool IsMd5(string? value) => value is { Length: 32 } && value.All(Uri.IsHexDigit);
}
