namespace RATools.Application.Publishing.Validation.Pdf;

public interface IPdfInspector
{
    PdfInspectionResult Inspect(Stream pdfStream, string relativeHref);
}

public sealed record PdfInspectionResult(
    string? PdfVersion,
    bool IsEncrypted,
    bool HasSecurityRestrictions,
    bool HasSearchableText,
    bool AllFontsEmbedded,
    IReadOnlyList<string> NonEmbeddedFonts,
    bool HasBookmarks,
    IReadOnlyList<PdfLinkReference> Links,
    string? ParseError = null,
    int? PageCount = null);

public sealed record PdfLinkReference(
    PdfLinkKind Kind,
    string Target,
    int? SourcePageNumber);

public enum PdfLinkKind
{
    IntraDocument,
    InterDocument,
    External
}
