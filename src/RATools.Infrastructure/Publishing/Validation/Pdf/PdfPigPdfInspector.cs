using RATools.Application.Publishing.Validation.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.Publishing.Validation.Pdf;

/// <summary>
/// 诚实性约定：AllFontsEmbedded / HasSecurityRestrictions 是三态，检查器只报告
/// 能证实的事实——证实不了就报 null（unknown），绝不默认合规。此前实现硬编码
/// AllFontsEmbedded=true 且 catch-all 把损坏 PDF 报告成合规，字体嵌入校验形同虚设。
/// </summary>
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
            var fontEmbedding = InspectFontEmbedding(document, pages);

            return new PdfInspectionResult(
                document.Version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                document.IsEncrypted,
                // 权限限制存在的前提是加密字典存在：无 /Encrypt 即无限制（事实），
                // 有 /Encrypt 即存在安全处理器（按限制处理，避免读不到 P 位时误报合规）。
                HasSecurityRestrictions: document.IsEncrypted,
                hasSearchableText,
                fontEmbedding.AllEmbedded,
                fontEmbedding.NonEmbeddedFonts,
                hasBookmarks,
                links,
                PageCount: pages.Length);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 解析失败时所有"合规位"必须是未知/不利值：ParseError 会触发 PDF_PARSE_FAILED（High），
            // 三态字段报 null 而非假 true。
            return new PdfInspectionResult(
                null,
                IsEncrypted: false,
                HasSecurityRestrictions: null,
                HasSearchableText: false,
                AllFontsEmbedded: null,
                [],
                HasBookmarks: false,
                [],
                exception.Message);
        }
    }

    private static (bool? AllEmbedded, IReadOnlyList<string> NonEmbeddedFonts) InspectFontEmbedding(
        PdfDocument document,
        IReadOnlyList<UglyToad.PdfPig.Content.Page> pages)
    {
        var nonEmbedded = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var indeterminate = false;
        var anyFontSeen = false;

        foreach (var page in pages)
        {
            try
            {
                if (!TryGetDictionary(document, GetOrNull(page.Dictionary, NameToken.Resources), out var resources)
                    || !TryGetDictionary(document, GetOrNull(resources!, NameToken.Font), out var fontDictionary))
                {
                    continue;
                }

                foreach (var fontEntry in fontDictionary!.Data)
                {
                    if (!TryGetDictionary(document, fontEntry.Value, out var font))
                    {
                        indeterminate = true;
                        continue;
                    }

                    anyFontSeen = true;
                    var state = ClassifyFontEmbedding(document, font!);
                    if (state == FontEmbeddingState.NotEmbedded)
                    {
                        nonEmbedded.Add(DescribeFont(font!, fontEntry.Key));
                    }
                    else if (state == FontEmbeddingState.Unknown)
                    {
                        indeterminate = true;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                indeterminate = true;
            }
        }

        if (nonEmbedded.Count > 0)
        {
            // 已证实存在未嵌入字体：这是事实，优先于任何"无法判定"。
            return (false, nonEmbedded.ToArray());
        }

        if (indeterminate)
        {
            return (null, []);
        }

        // 全部字体都证实嵌入；无字体（纯图像页）时视为真空真。
        _ = anyFontSeen;
        return (true, []);
    }

    private enum FontEmbeddingState
    {
        Embedded,
        NotEmbedded,
        Unknown,
    }

    private static FontEmbeddingState ClassifyFontEmbedding(PdfDocument document, DictionaryToken font)
    {
        var subtype = ResolveToken(document, GetOrNull(font, NameToken.Subtype)) as NameToken;

        // Type3 字体的字形是包内容流，自包含，无嵌入问题。
        if (subtype is not null && subtype.Equals(NameToken.Type3))
        {
            return FontEmbeddingState.Embedded;
        }

        // Type0 复合字体：嵌入信息在 DescendantFonts 的 FontDescriptor 上。
        if (subtype is not null && subtype.Equals(NameToken.Type0))
        {
            if (GetOrNull(font, NameToken.DescendantFonts) is not { } descendantsToken
                || ResolveToken(document, descendantsToken) is not ArrayToken descendants
                || descendants.Length == 0)
            {
                return FontEmbeddingState.Unknown;
            }

            foreach (var descendantToken in descendants.Data)
            {
                if (!TryGetDictionary(document, descendantToken, out var descendant))
                {
                    return FontEmbeddingState.Unknown;
                }

                var state = ClassifyByFontDescriptor(document, descendant!);
                if (state != FontEmbeddingState.Embedded)
                {
                    return state;
                }
            }

            return FontEmbeddingState.Embedded;
        }

        return ClassifyByFontDescriptor(document, font);
    }

    private static FontEmbeddingState ClassifyByFontDescriptor(PdfDocument document, DictionaryToken font)
    {
        // 无 FontDescriptor（标准 14 字体如 Helvetica）= 未嵌入：eCTD 要求全部字体嵌入，
        // 标准 14 也不例外。
        if (GetOrNull(font, NameToken.FontDescriptor) is not { } descriptorToken)
        {
            return FontEmbeddingState.NotEmbedded;
        }

        if (!TryGetDictionary(document, descriptorToken, out var descriptor))
        {
            return FontEmbeddingState.Unknown;
        }

        var hasFontFile = descriptor!.ContainsKey(NameToken.FontFile)
            || descriptor.ContainsKey(NameToken.FontFile2)
            || descriptor.ContainsKey(NameToken.FontFile3);

        return hasFontFile ? FontEmbeddingState.Embedded : FontEmbeddingState.NotEmbedded;
    }

    private static string DescribeFont(DictionaryToken font, string resourceKey)
    {
        if (font.TryGet(NameToken.BaseFont, out var baseFontToken) && baseFontToken is NameToken baseFont)
        {
            return baseFont.Data;
        }

        return resourceKey;
    }

    private static IToken? GetOrNull(DictionaryToken dictionary, NameToken key)
        => dictionary.TryGet(key, out var token) ? token : null;

    private static bool TryGetDictionary(PdfDocument document, IToken? token, out DictionaryToken? dictionary)
    {
        dictionary = token is null ? null : ResolveToken(document, token) as DictionaryToken;
        return dictionary is not null;
    }

    private static IToken? ResolveToken(PdfDocument document, IToken? token)
    {
        return token is IndirectReferenceToken indirect
            ? document.Structure.GetObject(indirect.Data)?.Data
            : token;
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
