using System.Globalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class PackagePdfValidator(IPackagePdfInspector inspector, TimeProvider? timeProvider = null)
{
    private static readonly string[] RuleIds = ["PDF-READABLE", "PDF-SECURITY", "PDF-FONTS", "PDF-NAVIGATION", "PDF-LINKS", "PDF-VERSION"];
    private static readonly HashSet<string> CommonFonts = new(StringComparer.Ordinal)
    {
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic", "TimesNewRomanPSMT", "TimesNewRomanPS-BoldMT",
        "TimesNewRomanPS-ItalicMT", "TimesNewRomanPS-BoldItalicMT", "Arial", "ArialMT", "Arial-BoldMT", "Arial-ItalicMT",
        "Arial-BoldItalicMT", "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique", "Courier",
        "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Symbol", "ZapfDingbats"
    };

    public async Task<PackagePdfInspection> InspectAsync(PackageDeliveryInput input, PackageFileInspection files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(files);
        if (files.InputDigest != input.InputDigest || files.HistoryManifestDigest != input.Packages.Baseline.Digest ||
            files.LimitsDigest != input.Packages.Target.Limits.Digest() || files.RulesDigest != PackageValidationCatalog.Current.Digest)
            throw new ArgumentException("File observations must bind this exact delivery pair, history, rules and limits.", nameof(files));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(input.Packages.Target.Limits.MaxElapsedSeconds), timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        ArgumentException.ThrowIfNullOrWhiteSpace(inspector.EngineVersion);
        var run = new Inspection(input, files, inspector.EngineVersion, linked.Token);
        try
        {
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
            run.Read(inspector);
            run.CheckLinks();
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
        }
        catch (PdfInspectionLimitException)
        {
            run.Incomplete = true;
            run.Terminal("PDF_INSPECTION_LIMIT", "PDF inspection exceeded the configured aggregate resource limit.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            run.Incomplete = true;
            run.Terminal("PDF_INSPECTION_TIMEOUT", "PDF inspection exceeded its deadline; remaining checks are incomplete.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return run.Finish();
    }

    private sealed class Inspection(PackageDeliveryInput input, PackageFileInspection files, string inspectorVersion, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, PackageInputFile> _inventory = input.Packages.Sequences.Values.SelectMany(capture => capture.Manifest.Files)
            .ToDictionary(file => file.LogicalPath, StringComparer.Ordinal);
        private readonly Dictionary<string, PackagePdfFacts?> _documents = new(StringComparer.Ordinal);
        private readonly List<ValidationFinding> _findings = [];
        private readonly List<PackagePdfLinkResolution> _links = [];
        public bool Incomplete { get; set; }

        public void Read(IPackagePdfInspector inspector)
        {
            long pages = 0, links = 0;
            foreach (var path in _inventory.Keys.Where(path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = input.Packages.OpenRead(path);
                PackagePdfFacts facts;
                try { facts = inspector.Inspect(stream, path, input.Packages.Target.Limits, cancellationToken); }
                catch (Exception exception) when (exception is not (OperationCanceledException or PdfInspectionLimitException or PackageInputChangedException))
                {
                    _documents.Add(path, null);
                    Add("PDF-READABLE", CheckStatus.NotEvaluated, "PDF_INSPECTOR_FAILED", "PDF inspection failed: " + exception.Message, Location(path));
                    foreach (var rule in RuleIds.Where(rule => rule != "PDF-READABLE"))
                        Add(rule, CheckStatus.NotEvaluated, "PDF_PREREQUISITE_UNRESOLVED", "PDF facts could not be obtained.", Location(path));
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                _documents.Add(path, facts);
                pages += facts.Basics.PageCount ?? 0;
                links += facts.Links.Count;
                if (pages > input.Packages.Target.Limits.MaxPdfPages || links > input.Packages.Target.Limits.MaxPdfLinks)
                    throw new PdfInspectionLimitException("Aggregate PDF pages or links exceeded.");
                InspectFacts(path, facts);
            }
        }

        private void InspectFacts(string path, PackagePdfFacts facts)
        {
            var location = Location(path);
            var basic = facts.Basics;
            if (basic.ParseError is not null || basic.PageCount is null or < 1)
            {
                Add("PDF-READABLE", basic.ParseError is null ? CheckStatus.NotEvaluated : CheckStatus.Fail, "PDF_PARSE_FAILED",
                    basic.ParseError ?? "PDF page count could not be established.", location);
                foreach (var rule in RuleIds.Where(rule => rule != "PDF-READABLE"))
                    Add(rule, CheckStatus.NotEvaluated, "PDF_PREREQUISITE_UNRESOLVED", "The PDF was not parsed completely.", location);
                return;
            }
            if (basic.IsEncrypted || basic.HasSecurityRestrictions == true)
                Add("PDF-SECURITY", CheckStatus.Fail, "PDF_SECURITY_RESTRICTED", "ICH Appendix 7 requires no PDF security settings or password protection.", location);
            else if (basic.HasSecurityRestrictions is null)
                Add("PDF-SECURITY", CheckStatus.NotEvaluated, "PDF_SECURITY_UNKNOWN", "PDF permissions could not be established.", location);
            if (basic.PdfVersion != "1.4")
                Add("PDF-VERSION", CheckStatus.NotEvaluated, "PDF_VERSION_REQUIRES_REGIONAL_POLICY", "ICH explicitly accepts PDF 1.4; other versions require qualified regional guidance.", location, actual: basic.PdfVersion);
            if (basic.AllFontsEmbedded == false && basic.NonEmbeddedFonts.Any(font => !CommonFonts.Contains(font)))
                Add("PDF-FONTS", CheckStatus.Fail, "PDF_ADDITIONAL_FONT_NOT_EMBEDDED", "A font outside the ICH common-font set was observed without embedding.", location,
                    actual: string.Join(", ", basic.NonEmbeddedFonts.Where(font => !CommonFonts.Contains(font))));
            Add("PDF-FONTS", CheckStatus.NotEvaluated, "PDF_FONT_COVERAGE_INCOMPLETE", "Font observations do not yet establish all nested resources, full-font versus subset embedding and regional exceptions.", location);
            Add("PDF-NAVIGATION", CheckStatus.NotEvaluated, "PDF_NAVIGATION_CONTENT_REVIEW_REQUIRED", "TOC/bookmark correspondence, page labels and view settings require further content-level verification.", location);
            if (basic.BookmarkMaxDepth > 4)
                Add("PDF-NAVIGATION", CheckStatus.Pass, "PDF_BOOKMARK_DEPTH_RECOMMENDATION", "ICH recommends no more than four bookmark levels.", location,
                    actual: basic.BookmarkMaxDepth.Value.ToString(CultureInfo.InvariantCulture), severity: ValidationSeverity.Info);
            if (!facts.NavigationComplete)
                Add("PDF-LINKS", CheckStatus.NotEvaluated, "PDF_LINK_INVENTORY_INCOMPLETE", string.Join(" ", facts.IncompleteReasons), location);
        }

        public void CheckLinks()
        {
            foreach (var (path, facts) in _documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (facts?.Basics.ParseError is not null || facts is null) continue;
                foreach (var link in facts.Links) CheckLink(path, link);
            }
            // XML href fragments point into the same inspected document namespace.
            foreach (var reference in files.References.Where(reference => reference.Exists && reference.Resolved?.Fragment is not null))
            {
                var resolved = reference.Resolved!;
                if (_documents.ContainsKey(resolved.LogicalPath!))
                    CheckLink(reference.SourceLogicalPath, new("XML href fragment", null,
                        new(Uri: reference.Reference)), reference.Location);
                else Add("PDF-LINKS", CheckStatus.NotEvaluated, "NON_PDF_FRAGMENT_UNSUPPORTED", "The referenced format's fragment syntax is not covered by the PDF inspector.", reference.Location);
            }
        }

        private void CheckLink(string source, PdfNavigationLink link, ValidationLocation? xmlLocation = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = xmlLocation ?? Location(source) with { FieldPath = link.ObjectPath + (link.SourcePageNumber is { } page ? $"; page={page}" : "") };
            string? targetPath = source;
            var target = link.Target;
            var start = _findings.Count;
            void Issue(CheckStatus status, string code, string message) => Add("PDF-LINKS", status, code, message, location);
            if (target.Invalid) Issue(CheckStatus.Fail, "PDF_DESTINATION_INVALID", "The PDF action contains an invalid file or destination.");
            if (target.UnsupportedReason is not null) Issue(CheckStatus.NotEvaluated, "PDF_ACTION_UNSUPPORTED", target.UnsupportedReason);
            if (target.Invalid || target.UnsupportedReason is not null) { Record(); return; }

            if (target.Uri is not null || target.FileSpecification is not null)
            {
                PackageReference reference;
                try
                {
                    // A PDF file specification is a literal filename, not a URI. Encode its
                    // segments before sharing the once-decoding logical URI resolver.
                    var value = target.Uri ?? string.Join('/', target.FileSpecification!.Split('/').Select(part => part is "." or ".." ? part : Uri.EscapeDataString(part)));
                    if (target.FileSpecification is { } file && (file.Contains('\\') || file.StartsWith('/') || file.Contains(':')))
                        throw new ArgumentException("Absolute or platform-specific PDF file specification.");
                    reference = PackageLogicalPath.ResolveReference(source, value);
                }
                catch (ArgumentException)
                {
                    Issue(CheckStatus.Fail, "PDF_LINK_OUTSIDE_APPLICATION", "The PDF link is malformed, absolute or escapes the application.");
                    Record(); return;
                }
                if (reference.ExternalUri is not null)
                {
                    targetPath = null;
                    Issue(CheckStatus.Pass, "PDF_EXTERNAL_LINK", "External URL classified without network access.");
                    Record(); return;
                }
                targetPath = reference.LogicalPath;
                if (reference.Query is not null) Issue(CheckStatus.NotEvaluated, "PDF_LINK_QUERY_UNSUPPORTED", "The link query requires format-specific evaluation.");
                if (target.Uri is not null && reference.Fragment is { } fragment) target = Fragment(fragment, target);
                if (target.UnsupportedReason is not null) Issue(CheckStatus.NotEvaluated, "PDF_FRAGMENT_UNSUPPORTED", target.UnsupportedReason);
                if (target.Invalid) Issue(CheckStatus.Fail, "PDF_DESTINATION_INVALID", "The link fragment contains invalid or conflicting destination parameters.");
            }
            var sequence = targetPath!.Split('/')[0];
            if (sequence.Length != 4 || sequence.Any(character => character is < '0' or > '9') ||
                string.CompareOrdinal(sequence, source.Split('/')[0]) > 0)
            { Issue(CheckStatus.Fail, "PDF_LINK_OUTSIDE_APPLICATION", "The PDF link refers outside this application's available sequence timeline."); Record(); return; }
            if (!input.Packages.Sequences.ContainsKey(sequence))
            { Issue(CheckStatus.NotEvaluated, "PDF_LINK_HISTORY_MISSING", "The referenced historical sequence is not in the explicit baseline."); Record(); return; }
            if (!_inventory.ContainsKey(targetPath))
            { Issue(CheckStatus.Fail, "PDF_LINK_FILE_MISSING", "The exact linked file does not exist in the selected input."); Record(); return; }
            if (target.PageNumber is null && target.NamedDestination is null) { Record(); return; }
            if (!_documents.TryGetValue(targetPath, out var document) || document?.Basics.ParseError is not null || document is null)
            { Issue(CheckStatus.NotEvaluated, "PDF_LINK_TARGET_UNREADABLE", "The linked PDF could not provide destination facts."); Record(); return; }
            var pageNumber = target.PageNumber;
            if (target.NamedDestination is { } name)
            {
                var matches = document.Destinations.Where(destination => destination.Name == name).ToArray();
                if (matches.Length > 1) Issue(CheckStatus.Fail, "PDF_NAMED_DESTINATION_AMBIGUOUS", "The named destination is defined more than once.");
                else if (matches.Length == 0)
                    Issue(document.DestinationsComplete ? CheckStatus.Fail : CheckStatus.NotEvaluated, document.DestinationsComplete ? "PDF_NAMED_DESTINATION_MISSING" : "PDF_DESTINATION_INVENTORY_INCOMPLETE",
                        "The exact named destination could not be established in the target PDF.");
                else if (matches[0].Invalid) Issue(CheckStatus.Fail, "PDF_DESTINATION_INVALID", "The named destination refers to an invalid page.");
                else pageNumber = matches[0].PageNumber;
                if (matches.Length == 1 && pageNumber is null) Issue(CheckStatus.NotEvaluated, "PDF_DESTINATION_UNRESOLVED", "The named destination did not resolve to a known page.");
            }
            if (pageNumber is { } number)
            {
                if (document.Basics.PageCount is not { } count) Issue(CheckStatus.NotEvaluated, "PDF_PAGE_COUNT_UNKNOWN", "The target PDF's page count is unknown.");
                else if (number < 1 || number > count) Issue(CheckStatus.Fail, "PDF_LINK_PAGE_OUT_OF_RANGE", "The target page is outside the target PDF's page range.");
            }
            Record();

            void Record()
            {
                if (_links.Count >= input.Packages.Target.Limits.MaxPdfLinks) throw new PdfInspectionLimitException("Aggregate resolved PDF links exceeded.");
                var observations = _findings.Skip(start).ToArray();
                _links.Add(new(source, link, targetPath, observations.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                    observations.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : CheckStatus.Pass));
            }
        }

        private static PdfDestinationTarget Fragment(string fragment, PdfDestinationTarget original)
        {
            if (fragment.Length == 0) return original with { Invalid = true };
            if (!fragment.Contains('=')) return original with { NamedDestination = fragment };
            var target = original;
            var destinations = 0;
            foreach (var parameter in fragment.Split('&'))
            {
                var pair = parameter.Split('=', 2);
                if (pair.Length != 2) return target with { Invalid = true };
                switch (pair[0])
                {
                    case "page":
                        destinations++;
                        if (!int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 1) return target with { Invalid = true };
                        target = target with { PageNumber = page };
                        break;
                    case "nameddest": destinations++; target = target with { NamedDestination = pair[1], Invalid = pair[1].Length == 0 }; break;
                    default: target = target with { UnsupportedReason = "PDF open parameter is not covered: " + pair[0] }; break;
                }
            }
            return destinations > 1 ? target with { Invalid = true } : target;
        }

        private void Add(string rule, CheckStatus status, string code, string message, ValidationLocation location,
            string? actual = null, ValidationSeverity? severity = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_findings.Count >= input.Packages.Target.Limits.MaxXmlFindings - 1) throw new PdfInspectionLimitException("PDF finding limit exceeded.");
            _findings.Add(new(rule, status, severity ?? (status == CheckStatus.Fail ? ValidationSeverity.Error : status == CheckStatus.Pass ? ValidationSeverity.Info : ValidationSeverity.Warning), code,
                string.IsNullOrWhiteSpace(message) ? "PDF inspection is incomplete." : message, location, Actual: actual));
        }
        public void Terminal(string code, string message) => _findings.Add(new("PDF-READABLE", CheckStatus.NotEvaluated, ValidationSeverity.Error, code, message, new()));
        public PackagePdfInspection Finish()
        {
            var hasPdf = _inventory.Keys.Any(path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            var checks = RuleIds.Select(rule =>
            {
                var findings = _findings.Where(finding => finding.RuleId == rule).ToArray();
                var status = findings.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail : Incomplete ||
                    findings.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : hasPdf ? CheckStatus.Pass : CheckStatus.NotApplicable;
                return new RuleCheckResult(rule, status, "PDF checks retain observed facts and all unsupported conditions; see located findings.", findings);
            });
            return new(files, inspectorVersion, _documents.Select(pair => new PackagePdfDocument(pair.Key, pair.Value)), _links, checks);
        }
        private static ValidationLocation Location(string path) => new(path.Split('/')[0], path);
    }
}
