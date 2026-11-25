using System.Globalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfBookmarkPageRule
{
    internal const string RuleId = "FDA47-5102";
    internal sealed class Inspection
    {
        private sealed record Bookmark(string Source, string ObjectPath, string Target, long Index);
        private readonly PackageDeliveryInput _input;
        private readonly CancellationToken _token;
        private readonly Action<RuleCheckResult>? _progress;
        private readonly Dictionary<string, PackageInputFile> _expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _current = new(StringComparer.Ordinal);
        private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _pageCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Bookmark>> _pending = new(StringComparer.Ordinal);
        private readonly List<ValidationFinding> _findings = [];
        private readonly ValidationRuleDescriptor? _rule;
        private RuleCheckResult? _terminal;
        private int _work;
        private int _links;
        public int FindingCount => _findings.Count;

        internal Inspection(PackageDeliveryInput input, PackageFileInspection files, PackageStandardsContext context,
            CancellationToken token, Action<RuleCheckResult>? progress = null)
        {
            _input = input; _token = token; _progress = progress; _work = input.Packages.Target.Limits.MaxPdfObjects;
            _rule = context.Catalog.ForProfile(files.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
            if (_rule is null) return;
            _terminal = Unknown("Qualified bookmark destination or complete target page evidence is unavailable.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "5102" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2013, 4, 30))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selection precedes FDA 5102."); return; }
            if (files.InputDigest != input.InputDigest || files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
                files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != input.Packages.Target.Limits.Digest()) return;
            foreach (var capture in input.Packages.Sequences.Values)
                foreach (var file in capture.Manifest.Files)
                {
                    token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                    if (!file.LogicalPath.StartsWith(capture.Manifest.SequenceNumber + "/", StringComparison.Ordinal)) return;
                    if (!file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!_expected.TryAdd(file.LogicalPath, file)) return;
                    if (capture == input.Packages.Target) _current.Add(file.LogicalPath);
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
                facts.Basics.IsEncrypted || facts.Basics.PageCount is null or < 1) return;
            if (facts.QualifiedPageCount is > 0 && facts.QualifiedPageCount == facts.Basics.PageCount)
            {
                var count = facts.QualifiedPageCount.Value;
                _pageCounts.Add(document.LogicalPath, count);
                if (_pending.TryGetValue(document.LogicalPath, out var waiting))
                    foreach (var bookmark in waiting)
                    {
                        _token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                        if (bookmark.Index < count) continue;
                        if (remaining <= 0) return;
                        Publish(bookmark, count); remaining--;
                    }
            }
            if (!_current.Contains(document.LogicalPath) || !facts.Basics.HasBookmarks) return;
            foreach (var bookmark in facts.BookmarkFiles)
            {
                _token.ThrowIfCancellationRequested();
                if (_work-- <= 0 || ++_links > _input.Packages.Target.Limits.MaxPdfLinks) return;
                if (bookmark.ZeroBasedPageIndex is not { } index || index < 0 || string.IsNullOrWhiteSpace(bookmark.ObjectPath) ||
                    string.IsNullOrWhiteSpace(bookmark.FileSpecification)) continue;
                var file = bookmark.FileSpecification;
                if (file.Contains('\\') || file.StartsWith('/') || file.Contains(':')) continue;
                string? target;
                try
                {
                    var value = string.Join('/', file.Split('/').Select(part => part is "." or ".." ? part : Uri.EscapeDataString(part)));
                    target = PackageLogicalPath.ResolveReference(document.LogicalPath, value).LogicalPath;
                }
                catch (ArgumentException) { continue; }
                if (target is null || !_expected.ContainsKey(target) || string.CompareOrdinal(target.Split('/')[0], _input.Packages.Target.Manifest.SequenceNumber) > 0) continue;
                var pending = new Bookmark(document.LogicalPath, bookmark.ObjectPath, target, index);
                if (_pageCounts.TryGetValue(target, out var count))
                {
                    if (index < count) continue;
                    if (remaining <= 0) return;
                    Publish(pending, count); remaining--;
                }
                else
                {
                    if (!_pending.TryGetValue(target, out var waiting)) _pending.Add(target, waiting = []);
                    waiting.Add(pending);
                }
            }
        }

        private void Publish(Bookmark bookmark, int count)
        {
            var targetCapture = _input.Packages.Sequences[bookmark.Target.Split('/')[0]];
            _findings.Add(new(RuleId, CheckStatus.Fail, _rule!.Severity, RuleId,
                "A qualified remote bookmark page index is outside the complete target PDF page tree.",
                new(_input.Packages.Target.Manifest.SequenceNumber, bookmark.Source, FieldPath: bookmark.ObjectPath + "; destination-page",
                    Files: [new(_input.Packages.Target.SourceId, bookmark.Source), new(targetCapture.SourceId, bookmark.Target)]),
                "Zero-based page index below " + count.ToString(CultureInfo.InvariantCulture),
                bookmark.Target + "; zero-based-index=" + bookmark.Index.ToString(CultureInfo.InvariantCulture),
                "Corrective action may be necessary and you will be contacted if it is necessary. Ensure that documents in future submissions do not have bookmarks linking to non-existent pages or named destinations."));
            _progress?.Invoke(new(RuleId, CheckStatus.Fail, "An out-of-range bookmark page is established; other destinations may remain unknown.", _findings.ToArray()));
            _token.ThrowIfCancellationRequested();
        }

        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : !interrupted && _current.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.NotEvaluated,
                "Only qualified current GoToR Fit page indexes and complete independently capture-bound target page trees are covered. Named destinations, internal bookmarks, other destination modes, missing/unreadable targets, unselected history and historical source PDFs remain unqualified; no Pass is emitted.", _findings.ToArray());
        }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
}
