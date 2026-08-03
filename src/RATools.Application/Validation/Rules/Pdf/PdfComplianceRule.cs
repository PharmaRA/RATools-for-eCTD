using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Validation.Pdf;

namespace RATools.Application.Validation.Rules.Pdf;

public sealed class PdfComplianceRule(IPdfInspector pdfInspector) : IEctdValidationRule
{
    // 短文档无书签不发 finding 的页数下限，以及书签层级的推荐上限。
    private const int BookmarkRequiredPageCount = 5;
    private const int MaxRecommendedBookmarkDepth = 4;

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
                result = new PdfInspectionResult(null, false, null, false, null, [], false, [], exception.Message);
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
        else if (!string.IsNullOrWhiteSpace(result.PdfVersion) && IsLegacyPdfVersion(result.PdfVersion))
        {
            // 1.4–1.7 是审评惯用安全区间；更老的版本仍可读，但功能受限，提示而不阻断。
            yield return Finding(
                "PDF_VERSION_LEGACY",
                EctdValidationSeverity.Low,
                leaf,
                $"PDF '{leaf.FileName}' uses legacy version {result.PdfVersion}, older than the customary 1.4-1.7 range.",
                "Consider re-saving the PDF as version 1.4 or later before publishing.");
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

        if (result.HasSecurityRestrictions == true)
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

        if (result.AllFontsEmbedded == false)
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
        else if (result.AllFontsEmbedded is null)
        {
            // 无法判定 ≠ 合规：字体嵌入是 eCTD 强制项，检查器判定失败时
            // 发出可见的低危提示，交由审阅人自行核实，而不是静默放行。
            yield return Finding(
                "PDF_FONT_EMBEDDING_UNVERIFIED",
                EctdValidationSeverity.Low,
                leaf,
                $"PDF '{leaf.FileName}' font embedding could not be verified automatically.",
                "Manually confirm that all fonts are embedded before submission.");
        }

        // 书签只对有一定篇幅的文档才是导航必需品：1 页封面信没有书签不该被烦扰。
        // PageCount 未知（null）时按"可能需要"处理，保持无法判定 ≠ 合规。
        if (!result.HasBookmarks && result.PageCount is null or >= BookmarkRequiredPageCount)
        {
            yield return Finding(
                "PDF_NO_BOOKMARKS",
                EctdValidationSeverity.Medium,
                leaf,
                $"PDF '{leaf.FileName}' has no bookmarks.",
                "Add bookmarks or a table of contents when required for reviewer navigation.");
        }

        if (result.BookmarkMaxDepth is { } bookmarkDepth && bookmarkDepth > MaxRecommendedBookmarkDepth)
        {
            yield return Finding(
                "PDF_BOOKMARK_TOO_DEEP",
                EctdValidationSeverity.Low,
                leaf,
                $"PDF '{leaf.FileName}' nests bookmarks {bookmarkDepth} levels deep, beyond the recommended {MaxRecommendedBookmarkDepth}.",
                $"Flatten the bookmark hierarchy to at most {MaxRecommendedBookmarkDepth} levels for reviewer navigation.");
        }

        // 有书签却不以书签面板打开，审评人看不到导航结构；PageMode 读不到（null）时不发 finding。
        if (result.HasBookmarks
            && result.PageMode is { } pageMode
            && !string.Equals(pageMode, "UseOutlines", StringComparison.Ordinal))
        {
            yield return Finding(
                "PDF_INITIAL_VIEW_NOT_OUTLINES",
                EctdValidationSeverity.Low,
                leaf,
                $"PDF '{leaf.FileName}' has bookmarks but its initial view is '{pageMode}' instead of the bookmarks panel.",
                "Set the PDF initial view to show the bookmarks panel (PageMode UseOutlines).");
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

    private static bool IsLegacyPdfVersion(string pdfVersion)
    {
        var normalized = pdfVersion.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) && version < new Version(1, 4);
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
