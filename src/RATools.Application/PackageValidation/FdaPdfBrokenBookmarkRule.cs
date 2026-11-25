using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfBrokenBookmarkRule
{
    internal const string RuleId = "FDA47-5100";
    internal sealed class Inspection
    {
        private readonly PackageDeliveryInput _input;
        private readonly CancellationToken _token;
        private readonly Action<RuleCheckResult>? _progress;
        private readonly Dictionary<string, PackageInputFile> _expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _inventory = new(StringComparer.Ordinal);
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
            _terminal = Unknown("Qualified bookmark identity, target inventory or inspection is incomplete.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "5100" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2013, 4, 30))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5100."); return; }
            if (files.InputDigest != input.InputDigest || files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
                files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != input.Packages.Target.Limits.Digest()) return;
            foreach (var capture in input.Packages.Sequences.Values)
                foreach (var file in capture.Manifest.Files)
                {
                    token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                    if (!file.LogicalPath.StartsWith(capture.Manifest.SequenceNumber + "/", StringComparison.Ordinal) || !_inventory.Add(file.LogicalPath)) return;
                    if (capture == input.Packages.Target && file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !_expected.TryAdd(file.LogicalPath, file)) return;
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
            foreach (var bookmark in facts.BookmarkFiles)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0 || remaining <= 0) return;
                if (string.IsNullOrWhiteSpace(bookmark.ObjectPath) || string.IsNullOrWhiteSpace(bookmark.FileSpecification)) continue;
                var file = bookmark.FileSpecification;
                if (file.Contains('\\') || file.StartsWith('/') || file.Contains(':')) continue;
                string? target;
                try
                {
                    // A file specification is literal. Percent signs, # and ? are not URI syntax.
                    var value = string.Join('/', file.Split('/').Select(part => part is "." or ".." ? part : Uri.EscapeDataString(part)));
                    target = PackageLogicalPath.ResolveReference(document.LogicalPath, value).LogicalPath;
                }
                catch (ArgumentException) { continue; }
                if (target is null) continue;
                var sequence = target.Split('/')[0];
                if (string.CompareOrdinal(sequence, _input.Packages.Target.Manifest.SequenceNumber) > 0 ||
                    !_input.Packages.Sequences.ContainsKey(sequence) || _inventory.Contains(target)) continue;
                _findings.Add(new(RuleId, CheckStatus.Fail, _rule.Severity, RuleId,
                    "A qualified bookmark points to a file absent from the explicitly selected captured sequence.",
                    new(_input.Packages.Target.Manifest.SequenceNumber, document.LogicalPath, FieldPath: bookmark.ObjectPath,
                        Files: [new(_input.Packages.Target.SourceId, document.LogicalPath)]), "Existing target file", target,
                    "Corrective action may be necessary and you will be contacted if it is necessary. Ensure that documents in future submissions do not have broken bookmarks."));
                remaining--;
                _progress?.Invoke(new(RuleId, CheckStatus.Fail, "A missing bookmark target is established; other bookmarks may remain unknown.", _findings.ToArray()));
                _token.ThrowIfCancellationRequested();
            }
        }

        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : !interrupted && _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.NotEvaluated,
                "Only qualified current-PDF GoToR bookmark file targets are covered. Unselected history is unknown, not missing; existing unreadable files are not 5100 failures. Other actions, file specifications, historical source PDFs and non-PDF-named contents remain unqualified; no Pass is emitted.", _findings.ToArray());
        }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
