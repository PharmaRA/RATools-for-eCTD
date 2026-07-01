using RATools.Application.Publishing.Validation.Pdf;
using UglyToad.PdfPig;

namespace RATools.Infrastructure.Publishing.Validation.Pdf;

public sealed class PdfPigPdfInspector : IPdfInspector
{
    public PdfInspectionResult Inspect(Stream pdfStream, string relativeHref)
    {
        try
        {
            using var document = PdfDocument.Open(pdfStream);
            var pages = document.GetPages().ToArray();
            var hasSearchableText = pages.Any(page => !string.IsNullOrWhiteSpace(page.Text));
            var links = pages
                .SelectMany(page => page.GetHyperlinks().Select(link => MapHyperlink(link.Uri, page.Number)))
                .Where(link => link is not null)
                .Cast<PdfLinkReference>()
                .ToArray();
            var hasBookmarks = document.TryGetBookmarks(out var bookmarks) && bookmarks.Roots.Count > 0;

            return new PdfInspectionResult(
                document.Version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                document.IsEncrypted,
                HasSecurityRestrictions: false,
                hasSearchableText,
                AllFontsEmbedded: true,
                [],
                hasBookmarks,
                links,
                PageCount: pages.Length);
        }
        catch (Exception exception)
        {
            return new PdfInspectionResult(
                null,
                IsEncrypted: false,
                HasSecurityRestrictions: false,
                HasSearchableText: false,
                AllFontsEmbedded: true,
                [],
                HasBookmarks: false,
                [],
                exception.Message);
        }
    }

    private static PdfLinkReference? MapHyperlink(string? uri, int sourcePageNumber)
    {
        if (uri is null)
        {
            return null;
        }

        var target = uri;
        var kind = target.StartsWith('#')
            ? PdfLinkKind.IntraDocument
            : Uri.TryCreate(target, UriKind.Absolute, out var parsedUri) && !parsedUri.IsFile
            ? PdfLinkKind.External
            : PdfLinkKind.InterDocument;
        return new PdfLinkReference(kind, target, sourcePageNumber);
    }
}
