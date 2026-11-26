using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfVersionRule
{
    internal const string RuleId = "FDA47-5035";
    internal sealed class Inspection
    {
        private readonly PackageDeliveryInput _input;
        private readonly CancellationToken _token;
        private readonly Action<RuleCheckResult>? _progress;
        private readonly Dictionary<string, PackageInputFile> _expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
        private readonly List<ValidationFinding> _findings = [];
        private readonly ValidationRuleDescriptor? _rule;
        private RuleCheckResult? _terminal;
        private bool _incomplete;
        private int _work;
        public int FindingCount => _findings.Count;

        internal Inspection(PackageDeliveryInput input, PackageFileInspection files, PackageStandardsContext context,
            CancellationToken token, Action<RuleCheckResult>? progress = null)
        {
            _input = input; _token = token; _progress = progress; _work = input.Packages.Target.Limits.MaxPdfObjects;
            _rule = context.Catalog.ForProfile(files.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
            if (_rule is null) return;
            _terminal = Unknown("Version policy qualification or inspection is incomplete.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "5035" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2013, 4, 30))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5035."); return; }
            if (context.SelectionDate < new DateOnly(2016, 9, 21) ||
                !context.Catalog.Sources.TryGetValue("fda-pdf-specification", out var pdfSource) ||
                pdfSource.Kind != RuleSourceKind.RegulatorySpecification || pdfSource.Version != "4.1" || pdfSource.VerificationStatus != "Verified" ||
                pdfSource.Sha256 != "113faa128d34ca5c2b99184da2cf4da2e4359403204289892720c1f73afcae2a" || !context.HasArchivedOfficialSource(pdfSource)) return;
            if (files.InputDigest != input.InputDigest || files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
                files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != input.Packages.Target.Limits.Digest()) return;
            foreach (var file in input.Packages.Target.Manifest.Files)
            {
                token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                if (file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !_expected.TryAdd(file.LogicalPath, file)) return;
            }
            _terminal = null;
        }

        internal void Observe(PackagePdfDocument document, int remaining)
        {
            if (_rule is null || _terminal is not null) return;
            _token.ThrowIfCancellationRequested(); if (_work-- <= 0) { _incomplete = true; return; }
            if (!_expected.TryGetValue(document.LogicalPath, out var captured)) return;
            if (!_observed.Add(document.LogicalPath) || document.CapturedSha256 != captured.Sha256 || document.Facts is not { } facts ||
                facts.ContentProcessing != PdfContentProcessingStatus.Processed || facts.Basics.ParseError is not null || facts.Basics.PageCount is null or < 1 ||
                facts.Basics.IsEncrypted || facts.VersionDeclaration is not { } declaration ||
                declaration.HasExtensions || declaration.HasMetadata)
            { _incomplete = true; return; }
            var version = declaration.HeaderVersion;
            if (version is not ("1.0" or "1.1" or "1.2" or "1.3" or "1.4" or "1.5" or "1.6" or "1.7"))
            { _incomplete = true; return; }
            if (declaration.HasCatalogVersion)
            {
                // PDF Reference 1.7 Table 3.25: a direct Version name raises the
                // header version, never lowers it. Unknown values are not absence.
                if (declaration.CatalogVersion is not ("1.4" or "1.5" or "1.6" or "1.7"))
                { _incomplete = true; return; }
                if (string.CompareOrdinal(declaration.CatalogVersion, version) > 0) version = declaration.CatalogVersion;
            }
            else if (declaration.CatalogVersion is not null) { _incomplete = true; return; }
            // Parsers may expose the original header or the effective declaration;
            // neither permits a third, contradictory version to authorize a result.
            if (facts.Basics.PdfVersion != declaration.HeaderVersion && facts.Basics.PdfVersion != version)
            { _incomplete = true; return; }
            if (version is "1.4" or "1.5" or "1.6" or "1.7") return;
            if (version is not ("1.0" or "1.1" or "1.2" or "1.3") || remaining <= 0)
            { _incomplete = true; return; }
            _findings.Add(new(RuleId, CheckStatus.Fail, _rule.Severity, RuleId,
                "The unambiguous PDF version declaration is below the versions accepted by the captured FDA PDF specification.",
                new(_input.Packages.Target.Manifest.SequenceNumber, document.LogicalPath, FieldPath: "pdf-header/version", Files: [new(_input.Packages.Target.SourceId, document.LogicalPath)]),
                "PDF 1.4 through 1.7 under the qualified ordinary-PDF declaration scope", version,
                "No corrective action is necessary. Modify SOPs so documents in future submissions conform to the published standards."));
            _progress?.Invoke(new(RuleId, CheckStatus.Fail, "An unsupported ordinary PDF version declaration is established; other documents may remain unknown.", _findings.ToArray()));
            _token.ThrowIfCancellationRequested();
        }

        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : interrupted || _incomplete || _observed.Count != _expected.Count ? CheckStatus.NotEvaluated :
                _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.Pass,
                "Original ordinary-PDF headers and qualified direct Catalog Version names are compared using the later declaration. Other catalog values, extensions, metadata/PDF-A qualification, unsupported parser versions, actual feature conformance, historical references, non-PDF-named contents and pre-4.1 policy remain unqualified.", _findings.ToArray());
        }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
