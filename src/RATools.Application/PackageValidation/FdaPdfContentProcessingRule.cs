using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaPdfContentProcessingRule
{
    internal const string RuleId = "FDA47-3102";
    internal static RuleCheckResult? Inspect(PackageDeliveryInput input, PackageFileInspection files,
        IReadOnlyList<PackagePdfDocument> documents, PackageStandardsContext context, int remaining, CancellationToken cancellationToken,
        Action<RuleCheckResult>? retainProgress = null)
    {
        var run = new Inspection(input, files, context, cancellationToken, retainProgress);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var work = input.Packages.Target.Limits.MaxPdfObjects;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) return run.Finish(true);
            counts[document.LogicalPath] = counts.GetValueOrDefault(document.LogicalPath) + 1;
        }
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work-- <= 0) return run.Finish(true);
            if (counts[document.LogicalPath] != 1) { run.MarkIncomplete(); continue; }
            var before = run.FindingCount;
            run.Observe(document, remaining);
            remaining -= run.FindingCount - before;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return run.Finish(false);
    }

    // The PDF pipeline feeds each observed file once, before other PDF checks can
    // exhaust their allowance. Finish does not read files or use an expired token.
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
            _terminal = Unknown("The PDF content processing inspection did not complete.");
            progress?.Invoke(_terminal); token.ThrowIfCancellationRequested();
            if (_rule.AuthorityRuleId != "3102" || _rule.RuleVersion != "4.7.1" || _rule.SourceId != "fda-validation-criteria-4.7" ||
                !context.Catalog.Sources.TryGetValue(_rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
                source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
                !context.HasArchivedOfficialSource(source) || context.SelectionDate is null) return;
            if (context.SelectionDate < new DateOnly(2013, 4, 30))
            { _terminal = new(RuleId, CheckStatus.NotApplicable, "The selected date precedes FDA 3102."); return; }
            if (files.InputDigest != input.InputDigest || files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
                files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != input.Packages.Target.Limits.Digest()) return;
            foreach (var file in input.Packages.Target.Manifest.Files)
            {
                token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                if (!file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                if (!_expected.TryAdd(file.LogicalPath, file)) return;
            }
            _terminal = null;
        }

        internal void Observe(PackagePdfDocument document, int remaining)
        {
            if (_rule is null || _terminal is not null) return;
            _token.ThrowIfCancellationRequested();
            if (_work-- <= 0) { _incomplete = true; return; }
            if (!_expected.TryGetValue(document.LogicalPath, out var captured)) return;
            if (!_observed.Add(document.LogicalPath) || document.CapturedSha256 != captured.Sha256 || document.Facts is not { } facts)
            { _incomplete = true; return; }
            if (facts.ContentProcessing == PdfContentProcessingStatus.Processed && facts.Basics.ParseError is null && facts.Basics.PageCount is > 0) return;
            if (facts.ContentProcessing != PdfContentProcessingStatus.InvalidContent || facts.Basics.ParseError is null || facts.Basics.PageCount is not null || facts.Basics.IsEncrypted)
            { _incomplete = true; return; }
            if (remaining <= 0) { _incomplete = true; return; }
            _findings.Add(new(RuleId, CheckStatus.Fail, _rule.Severity, RuleId,
                "The bound current PDF has a definite unreadable content-format failure.",
                new(_input.Packages.Target.Manifest.SequenceNumber, document.LogicalPath, FieldPath: "pdf-content", Files: [new(_input.Packages.Target.SourceId, document.LogicalPath)]),
                "Processable PDF contents", "InvalidContent", "Provide a corrected valid PDF leaf with operation Replace in a future submission."));
            _progress?.Invoke(new(RuleId, CheckStatus.Fail, "A current PDF content failure is established; other documents may remain unknown.", _findings.ToArray()));
            _token.ThrowIfCancellationRequested();
        }

        internal void MarkIncomplete() => _incomplete = true;
        internal RuleCheckResult? Finish(bool interrupted)
        {
            if (_rule is null) return null;
            if (_terminal is not null) return _terminal;
            return new(RuleId, _findings.Count > 0 ? CheckStatus.Fail : interrupted || _incomplete || _observed.Count != _expected.Count ? CheckStatus.NotEvaluated : _expected.Count == 0 ? CheckStatus.NotApplicable : CheckStatus.Pass,
                "Checked typed current captured-PDF processing outcomes. Legacy parse messages, encrypted/input/limit/unsupported failures do not prove corruption. Historical reference scope, non-PDF-named content, complete parser qualification and isolation remain incomplete.", _findings.ToArray());
        }
    }
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
}
