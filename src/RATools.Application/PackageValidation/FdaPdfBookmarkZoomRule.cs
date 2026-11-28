using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfBookmarkZoomRule
{
    internal const string RuleId = "FDA47-5117";
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
        private int _work;
        public int FindingCount => _findings.Count;

        internal Inspection(PackageDeliveryInput input, PackageFileInspection files, PackageStandardsContext context,
            CancellationToken token, Action<RuleCheckResult>? progress = null)
        {
            _input = input; _token = token; _progress = progress; _work = input.Packages.Target.Limits.MaxPdfObjects;
            _rule = context.Catalog.ForProfile(files.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
            if (_rule is null) return;
            _terminal = Unknown("Qualified bookmark zoom evidence is incomplete.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "5117" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2013, 4, 30))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5117."); return; }
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
            if (!_expected.TryGetValue(document.LogicalPath, out var captured) || !_observed.Add(document.LogicalPath) ||
                document.CapturedSha256 != captured.Sha256 || document.Facts is not { } facts ||
                facts.ContentProcessing != PdfContentProcessingStatus.Processed || facts.Basics.ParseError is not null ||
                facts.Basics.PageCount is null or < 1 || facts.Basics.IsEncrypted || !facts.Basics.HasBookmarks) return;
            var paths = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var zoom in facts.NonInheritingBookmarkZooms)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                if (string.IsNullOrWhiteSpace(zoom.ObjectPath)) continue;
                paths[zoom.ObjectPath] = paths.GetValueOrDefault(zoom.ObjectPath) + 1;
            }
            foreach (var zoom in facts.NonInheritingBookmarkZooms)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0 || remaining <= 0) return;
                if (string.IsNullOrWhiteSpace(zoom.ObjectPath) || paths[zoom.ObjectPath] != 1 ||
                    zoom.Kind is not (PdfBookmarkZoomKind.FitPage or PdfBookmarkZoomKind.FixedFactor)) continue;
                _findings.Add(new(RuleId, CheckStatus.Fail, _rule.Severity, RuleId,
                    "A qualified bookmark explicitly selects a fit-page or fixed magnification instead of inheriting zoom.",
                    new(_input.Packages.Target.Manifest.SequenceNumber, document.LogicalPath, FieldPath: zoom.ObjectPath,
                        Files: [new(_input.Packages.Target.SourceId, document.LogicalPath)]), "Inherit Zoom", zoom.Kind.ToString(),
                    "Ensure that all bookmarks are set to 'Inherit Zoom'."));
                remaining--;
                _progress?.Invoke(new(RuleId, CheckStatus.Fail, "A non-inheriting bookmark zoom is established; other bookmarks may remain unknown.", _findings.ToArray()));
                _token.ThrowIfCancellationRequested();
            }
        }

        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : !interrupted && _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.NotEvaluated,
                "Only qualified current GoToR Fit and positive fixed XYZ zoom settings are covered under archived PDF 4.1. XYZ zero/null does not prove a violation. Internal/named destinations, other view modes/actions/file encodings, historical sources and earlier policy remain incomplete; no Pass is emitted.", _findings.ToArray());
        }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
