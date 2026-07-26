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
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {BuildContent(text).Length} >>\nstream\n{BuildContent(text)}\nendstream"
        };

        var builder = new StringBuilder();
        var offsets = new List<int> { 0 };
        builder.Append("%PDF-1.4\n");
        for (var index = 0; index < objects.Length; index += 1)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n");
            builder.Append(objects[index]).Append('\n');
            builder.Append("endobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n");
        builder.Append("0 ").Append(objects.Length + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer\n");
        builder.Append("<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n");
        builder.Append(xrefOffset).Append('\n');
        builder.Append("%%EOF\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string BuildContent(string text) => $"BT /F1 24 Tf 100 700 Td ({text}) Tj ET";
}
