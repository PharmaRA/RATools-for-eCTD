using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfFontEmbeddingRule
{
    internal const string RuleId = "FDA47-5005";
    // These PDF built-in families are explicitly different from Table 1's
    // Times New Roman / Arial / Courier New. Arbitrary aliases remain unknown.
    private static readonly HashSet<string> KnownNonStandard = new(StringComparer.Ordinal)
    {
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique"
    };

    internal sealed class Inspection
    {
        private readonly PackageDeliveryInput _input;
        private readonly CancellationToken _token;
        private readonly Dictionary<string, PackageInputFile> _expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
        private readonly List<ValidationFinding> _findings = [];
        private readonly ValidationRuleDescriptor? _rule;
        private readonly Action<RuleCheckResult>? _progress;
        private RuleCheckResult? _terminal;
        private int _work;
        public int FindingCount => _findings.Count;

        internal Inspection(PackageDeliveryInput input, PackageFileInspection files, PackageStandardsContext context,
            CancellationToken token, Action<RuleCheckResult>? progress = null)
        {
            _input = input; _token = token; _progress = progress; _work = input.Packages.Target.Limits.MaxPdfObjects;
            _rule = context.Catalog.ForProfile(files.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
            if (_rule is null) return;
            _terminal = Unknown("The font inspection is not qualified or did not complete.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "5005" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2013, 4, 30))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 5005."); return; }
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
            _token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
            if (!_expected.TryGetValue(document.LogicalPath, out var file) || !_observed.Add(document.LogicalPath) ||
                document.CapturedSha256 != file.Sha256 || document.Facts is not { } facts ||
                facts.ContentProcessing != PdfContentProcessingStatus.Processed || facts.Basics.ParseError is not null ||
                facts.Basics.PageCount is null or < 1 || facts.Basics.IsEncrypted) return;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            // Conflicting duplicate observations must not authorize a failure.
            foreach (var font in facts.Fonts)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0 || !paths.Add(font.ObjectPath)) return;
            }
            foreach (var font in facts.Fonts)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0 || remaining <= 0) return;
                if (font.Subtype != "Type1" || font.BaseFont is null || !KnownNonStandard.Contains(font.BaseFont) ||
                    font.Embedding != PdfFontEmbeddingStatus.Missing || string.IsNullOrWhiteSpace(font.ObjectPath)) continue;
                _findings.Add(new(RuleId, CheckStatus.Fail, _rule.Severity, RuleId,
                    "A used non-standard PDF built-in font is definitely not embedded.",
                    new(_input.Packages.Target.Manifest.SequenceNumber, document.LogicalPath, FieldPath: font.ObjectPath, Files: [new(_input.Packages.Target.SourceId, document.LogicalPath)]),
                    "Fully embedded non-standard font", font.BaseFont,
                    "Provide a corrected PDF leaf with operation Replace in a future submission, with all non-standard fonts fully embedded."));
                remaining--;
                _progress?.Invoke(new(RuleId, CheckStatus.Fail, "A definite non-standard unembedded font was found; other fonts may remain unknown.", _findings.ToArray()));
                _token.ThrowIfCancellationRequested();
            }
        }

        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : !interrupted && _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.NotEvaluated,
                "Only exact captured current PDFs with definite used simple-font evidence are checked. Arbitrary aliases, full versus subset font programs, nested/inherited resources, historical references, non-PDF-named contents and pre-4.1 policy remain unqualified; absence of a finding is not Pass.", _findings.ToArray());
        }
    }
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
}
