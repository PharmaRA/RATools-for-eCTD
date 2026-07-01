using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Validation.Pdf;

namespace RATools.Application.Validation.Rules.Pdf;

public sealed class PdfComplianceRule(IPdfInspector pdfInspector) : IEctdValidationRule
{
    public string RuleId => "PDF-COMPLIANCE";

    public string Category => "PdfCompliance";

    public EctdValidationSeverity DefaultSeverity => EctdValidationSeverity.High;

    public IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context)
    {
        if (context.Package is null)
        {
            yield break;
        }

        var publishedHrefs = context.Package.PublishedFiles
            .Select(file => NormalizeHref(file.Href))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var leaf in GetPdfLeaves(context.Package))
        {
            PdfInspectionResult result;
            try
            {
                using var stream = File.OpenRead(leaf.SourcePath);
                result = pdfInspector.Inspect(stream, leaf.Href);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                result = new PdfInspectionResult(null, false, false, false, true, [], false, [], exception.Message);
            }

            foreach (var finding in EvaluateLeaf(leaf, result, publishedHrefs))
            {
                yield return finding;
            }
        }
    }

    private static IEnumerable<EctdValidationFinding> EvaluateLeaf(
        EctdLeaf leaf,
        PdfInspectionResult result,
        HashSet<string> publishedHrefs)
    {
        if (!string.IsNullOrWhiteSpace(result.ParseError))
        {
            yield return Finding(
                "PDF_PARSE_FAILED",
                EctdValidationSeverity.High,
                leaf,
                $"PDF '{leaf.FileName}' could not be inspected: {result.ParseError}",
                "Open and repair or replace the PDF, then rerun publish readiness.");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(result.PdfVersion) && IsUnsupportedPdfVersion(result.PdfVersion))
        {
            yield return Finding(
                "PDF_VERSION_UNSUPPORTED",
                EctdValidationSeverity.High,
                leaf,
                $"PDF '{leaf.FileName}' uses unsupported version {result.PdfVersion}.",
                "Save the PDF as a supported FDA eCTD PDF version before publishing.");
        }

        if (result.IsEncrypted)
        {
            yield return Finding(
                "PDF_ENCRYPTED",
                EctdValidationSeverity.High,
                leaf,
                $"PDF '{leaf.FileName}' is encrypted.",
                "Remove encryption or password protection from the PDF before publishing.");
        }

        if (result.HasSecurityRestrictions)
        {
            yield return Finding(
                "PDF_SECURITY_RESTRICTED",
                EctdValidationSeverity.Medium,
                leaf,
                $"PDF '{leaf.FileName}' has security restrictions.",
                "Remove PDF security restrictions that prevent review operations.");
        }

        if (!result.HasSearchableText)
        {
            yield return Finding(
                "PDF_NO_SEARCHABLE_TEXT",
                EctdValidationSeverity.High,
                leaf,
                $"PDF '{leaf.FileName}' does not contain searchable text.",
                "Run OCR or replace the scanned image PDF with a searchable text PDF.");
        }

        if (!result.AllFontsEmbedded)
        {
            var fonts = result.NonEmbeddedFonts.Count == 0
                ? "unknown fonts"
                : string.Join(", ", result.NonEmbeddedFonts);
            yield return Finding(
                "PDF_FONT_NOT_EMBEDDED",
                EctdValidationSeverity.High,
                leaf,
                $"PDF '{leaf.FileName}' has non-embedded fonts: {fonts}.",
                "Embed all fonts before publishing the PDF.");
        }

        if (!result.HasBookmarks)
        {
            yield return Finding(
                "PDF_NO_BOOKMARKS",
                EctdValidationSeverity.Medium,
                leaf,
                $"PDF '{leaf.FileName}' has no bookmarks.",
                "Add bookmarks or a table of contents when required for reviewer navigation.");
        }

        foreach (var link in result.Links.Where(link => link.Kind == PdfLinkKind.IntraDocument))
        {
            if (TryGetIntraDocumentPageTarget(link.Target, out var targetPage)
                && result.PageCount is { } pageCount
                && (targetPage < 1 || targetPage > pageCount))
            {
                yield return Finding(
                    "PDF_BROKEN_INTRA_LINK",
                    EctdValidationSeverity.Medium,
                    leaf,
                    $"PDF '{leaf.FileName}' links to missing internal target '{link.Target}'.",
                    "Update the PDF internal link target so it points to an existing page or destination.");
            }
        }

        foreach (var link in result.Links.Where(link => link.Kind == PdfLinkKind.InterDocument))
        {
            var resolvedTarget = ResolveInterDocumentTarget(leaf.Href, link.Target);
            if (string.IsNullOrWhiteSpace(resolvedTarget) || !publishedHrefs.Contains(resolvedTarget))
            {
                yield return Finding(
                    "PDF_BROKEN_INTER_LINK",
                    EctdValidationSeverity.High,
                    leaf,
                    $"PDF '{leaf.FileName}' links to missing package file '{link.Target}'.",
                    "Update the PDF link target or include the referenced file in the eCTD package.");
            }
        }
    }

    private static EctdValidationFinding Finding(
        string ruleId,
        EctdValidationSeverity severity,
        EctdLeaf leaf,
        string message,
        string recommendedAction)
        => new(
            ruleId,
            "PdfCompliance",
            severity,
            message,
            recommendedAction,
            leaf.CtdSection,
            leaf.DocumentId,
            leaf.PlacementId);

    private static IEnumerable<EctdLeaf> GetPdfLeaves(EctdSequencePackage package)
        => package.Module1Leaves
            .Concat(package.IchBackboneLeaves)
            .Where(leaf => string.Equals(leaf.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                || leaf.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    private static bool IsUnsupportedPdfVersion(string pdfVersion)
    {
        var normalized = pdfVersion.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) && version > new Version(1, 7);
    }

    private static bool TryGetIntraDocumentPageTarget(string target, out int pageNumber)
    {
        pageNumber = 0;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var pageMarkerIndex = target.IndexOf("page=", StringComparison.OrdinalIgnoreCase);
        if (pageMarkerIndex < 0)
        {
            return false;
        }

        var pageStartIndex = pageMarkerIndex + "page=".Length;
        var pageEndIndex = target.IndexOfAny(['&', '#'], pageStartIndex);
        var pageText = pageEndIndex < 0
            ? target[pageStartIndex..]
            : target[pageStartIndex..pageEndIndex];
        return int.TryParse(pageText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out pageNumber);
    }

    private static string? ResolveInterDocumentTarget(string sourceHref, string target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || target.StartsWith('#')
            || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var targetWithoutAnchor = target.Split('#')[0];
        var sourceDirectory = GetDirectoryName(NormalizeHref(sourceHref));
        var combined = string.IsNullOrWhiteSpace(sourceDirectory)
            ? targetWithoutAnchor
            : $"{sourceDirectory}/{targetWithoutAnchor}";
        return NormalizeHref(CollapseRelativeSegments(combined));
    }

    private static string NormalizeHref(string href)
        => href.Replace('\\', '/').TrimStart('/').TrimStart('.', '/');

    private static string GetDirectoryName(string href)
    {
        var index = href.LastIndexOf('/');
        return index < 0 ? string.Empty : href[..index];
    }

    private static string CollapseRelativeSegments(string href)
    {
        var segments = new Stack<string>();
        foreach (var segment in href.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.Pop();
                }

                continue;
            }

            segments.Push(segment);
        }

        return string.Join('/', segments.Reverse());
    }
}
