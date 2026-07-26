namespace RATools.Application.Publishing.Validation.Pdf;

public interface IPdfInspector
{
    PdfInspectionResult Inspect(Stream pdfStream, string relativeHref);
}

/// <summary>
/// AllFontsEmbedded / HasSecurityRestrictions 为三态：true/false 是已验证的事实，
/// null 表示检查器无法判定。检查器绝不能把"无法判定"报告成合规——字体嵌入是
/// eCTD 强制项，假 true 会给出虚假合规信号。
/// </summary>
public sealed record PdfInspectionResult(
    string? PdfVersion,
    bool IsEncrypted,
    bool? HasSecurityRestrictions,
    bool HasSearchableText,
    bool? AllFontsEmbedded,
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
