using System.Globalization;
using System.Text;
using RATools.Infrastructure.Publishing.Validation.Pdf;

namespace RATools.Tests.Publishing.Validation.Pdf;

public sealed class PdfPigPdfInspectorTests
{
    [Fact]
    public void Inspect_ReturnsSearchableTextForReadablePdf()
    {
        var inspector = new PdfPigPdfInspector();
        using var stream = new MemoryStream(CreateTinyPdf("Hello searchable PDF"));

        var result = inspector.Inspect(stream, "m5/readable.pdf");

        Assert.Null(result.ParseError);
        Assert.Equal("1.4", result.PdfVersion);
        Assert.True(result.HasSearchableText);
        Assert.False(result.IsEncrypted);
    }

    [Fact]
    public void Inspect_ReturnsParseErrorInsteadOfThrowingForInvalidPdf()
    {
        var inspector = new PdfPigPdfInspector();
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a pdf"));

        var result = inspector.Inspect(stream, "m5/broken.pdf");

        Assert.NotNull(result.ParseError);
        Assert.False(result.HasSearchableText);
    }

    [Fact]
    public void Inspect_ReportsUnknownComplianceStateForInvalidPdf()
    {
        // 解析失败时三态字段必须是 null（无法判定），不能默认合规——
        // 旧实现硬编码 AllFontsEmbedded=true，损坏的 PDF 会被判为字体全嵌入。
        var inspector = new PdfPigPdfInspector();
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a pdf"));

        var result = inspector.Inspect(stream, "m5/broken.pdf");

        Assert.Null(result.AllFontsEmbedded);
        Assert.Null(result.HasSecurityRestrictions);
    }

    [Fact]
    public void Inspect_DetectsNonEmbeddedStandardFont()
    {
        // 测试样本使用标准 14 字体 Helvetica 且无 FontDescriptor/FontFile：
        // 按 eCTD 要求这是未嵌入字体，必须被检出而非默认放行。
        var inspector = new PdfPigPdfInspector();
        using var stream = new MemoryStream(CreateTinyPdf("Uses non-embedded Helvetica"));

        var result = inspector.Inspect(stream, "m5/non-embedded.pdf");

        Assert.Null(result.ParseError);
        Assert.False(result.AllFontsEmbedded);
        Assert.Contains("Helvetica", result.NonEmbeddedFonts);
    }

    [Fact]
    public void Inspect_ReportsNoSecurityRestrictionsForUnencryptedPdf()
    {
        var inspector = new PdfPigPdfInspector();
        using var stream = new MemoryStream(CreateTinyPdf("Plain unencrypted PDF"));

        var result = inspector.Inspect(stream, "m5/plain.pdf");

        Assert.False(result.IsEncrypted);
        Assert.False(result.HasSecurityRestrictions);
    }

    private static byte[] CreateTinyPdf(string text)
        => BuildPdf(text, bookmarkDepth: 0, pageMode: null);

    /// <summary>
    /// 构造最小可解析 PDF。bookmarkDepth &gt; 0 时生成一条 depth 层深的单链书签树
    /// （每层一个节点，节点 i 的 First/Last 指向节点 i+1），pageMode 非 null 时写入 catalog 的 /PageMode。
    /// </summary>
    private static byte[] BuildPdf(string text, int bookmarkDepth, string? pageMode)
    {
        // 对象编号：1 catalog、2 pages、3 page、4 font、5 contents、6 outlines 根、7.. 书签节点。
        const int OutlinesObjectNumber = 6;
        var firstBookmarkNumber = OutlinesObjectNumber + 1;

        var catalogEntries = new StringBuilder("<< /Type /Catalog /Pages 2 0 R");
        if (bookmarkDepth > 0)
        {
            catalogEntries.Append(CultureInfo.InvariantCulture, $" /Outlines {OutlinesObjectNumber} 0 R");
        }

        if (pageMode is not null)
        {
            catalogEntries.Append(CultureInfo.InvariantCulture, $" /PageMode /{pageMode}");
        }

        catalogEntries.Append(" >>");

        var objects = new List<string>
        {
            catalogEntries.ToString(),
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {BuildContent(text).Length} >>\nstream\n{BuildContent(text)}\nendstream"
        };

        if (bookmarkDepth > 0)
        {
            objects.Add($"<< /Type /Outlines /First {firstBookmarkNumber} 0 R /Last {firstBookmarkNumber} 0 R /Count 1 >>");
            for (var level = 0; level < bookmarkDepth; level += 1)
            {
                var self = firstBookmarkNumber + level;
                var parent = level == 0 ? OutlinesObjectNumber : self - 1;
                var item = new StringBuilder(
                    $"<< /Title (Level {level + 1}) /Parent {parent} 0 R /Dest [3 0 R /Fit]");
                if (level + 1 < bookmarkDepth)
                {
                    var child = self + 1;
                    item.Append(CultureInfo.InvariantCulture, $" /First {child} 0 R /Last {child} 0 R /Count 1");
                }

                item.Append(" >>");
                objects.Add(item.ToString());
            }
        }

        var builder = new StringBuilder();
        var offsets = new List<int> { 0 };
        builder.Append("%PDF-1.4\n");
        for (var index = 0; index < objects.Count; index += 1)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n");
            builder.Append(objects[index]).Append('\n');
            builder.Append("endobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n");
        builder.Append("0 ").Append(objects.Count + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer\n");
        builder.Append("<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n");
        builder.Append(xrefOffset).Append('\n');
        builder.Append("%%EOF\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string BuildContent(string text) => $"BT /F1 24 Tf 100 700 Td ({text}) Tj ET";
}
