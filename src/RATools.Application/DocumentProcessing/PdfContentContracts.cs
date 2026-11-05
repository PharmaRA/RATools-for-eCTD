using RATools.Domain.Documents;

namespace RATools.Application.DocumentProcessing;

public sealed record PdfOcrParameters(string Language, IReadOnlyList<int> Pages, int Dpi = 150);
public sealed record PdfContentParameters(PdfOcrParameters? Ocr, bool EmbedStandardFonts, string? TargetVersion)
{
    public PdfContentParameters Normalize()
    {
        if (Ocr is null && !EmbedStandardFonts && TargetVersion is null)
            throw new ArgumentException("Choose at least one qualified PDF content operation.");
        if (TargetVersion is not (null or "1.7")) throw new ArgumentException("Only normalization to PDF 1.7 is qualified.");
        if (Ocr is not null && (Ocr.Language != "eng" || Ocr.Dpi != 150 || Ocr.Pages is null || Ocr.Pages.Count is < 1 or > 500 ||
            Ocr.Pages.Any(page => page < 1) || !Ocr.Pages.SequenceEqual(Ocr.Pages.Distinct().Order())))
            throw new ArgumentException("OCR requires English at 150 DPI and a nonempty, unique, ascending list of pages.");
        return this with { Ocr = Ocr is null ? null : Ocr with { Pages = Ocr.Pages.ToArray() } };
    }
    public void ValidateAgainst(PdfContentInspection inspection)
    {
        _ = Normalize();
        if (!inspection.Capabilities.Enabled || inspection.BlockReasons.Count > 0 || inspection.PageCount is < 1 or > 500)
            throw new PdfProcessingRejectedException("The source or configured content engine is not qualified for processing.");
        if (Ocr is { } ocr && (!inspection.Capabilities.OcrAvailable || ocr.Pages.Any(page => page > inspection.PageCount)))
            throw new PdfProcessingRejectedException("OCR model availability or selected page range is invalid.");
        if (EmbedStandardFonts && (!inspection.Capabilities.FontEmbeddingAvailable || inspection.MissingFonts.Any(font => !font.Qualified)))
            throw new PdfProcessingRejectedException("The source contains missing fonts outside the qualified standard-font set.");
        if (TargetVersion is not null && !inspection.Capabilities.VersionNormalizationAvailable)
            throw new PdfProcessingRejectedException("PDF version normalization is not available.");
    }
}
public sealed record PdfContentCapabilities(bool Enabled, string? EngineBinding, bool OcrAvailable,
    bool FontEmbeddingAvailable, bool VersionNormalizationAvailable, string Language, int Dpi, int MaxPages,
    long MaxPixels, IReadOnlyList<string> BlockReasons, IReadOnlyList<string> UnverifiedProperties);
public sealed record PdfContentFontFact(string Name, bool Qualified);
public sealed record PdfContentInspection(Guid SourceVersionId, string Sha256, int PageCount, string SourceVersion,
    IReadOnlyList<int> SearchablePages, IReadOnlyList<int> ScannedPages, IReadOnlyList<PdfContentFontFact> MissingFonts,
    PdfContentCapabilities Capabilities, IReadOnlyList<string> BlockReasons);
public sealed record PdfContentInspectionRequest(Guid SourceVersionId, long ExpectedRevision);
public sealed record PdfContentOcrEvidence(string Language, int Dpi, string ModelSha256,
    IReadOnlyList<int> ProcessedPages, IReadOnlyList<int> AlreadySearchablePages, bool RequiresTextReview);
public sealed record PdfContentFontEvidence(string Name, string ProgramSha256, long ProgramBytes);
public sealed record PdfContentFontsEvidence(IReadOnlyList<PdfContentFontEvidence> Embedded, string RegulatoryConformance);
public sealed record PdfContentPageEvidence(int Page, IReadOnlyDictionary<string, string> RenderSha256, string TextSha256);
public sealed record PdfContentReceipt(string EngineVersion, string SourceSha256, PdfContentParameters Parameters,
    PdfContentOcrEvidence? Ocr, PdfContentFontsEvidence? Fonts, string RegulatoryConformance,
    IReadOnlyList<PdfContentPageEvidence> Pages, string OutputVersion, string OutputSha256, long OutputBytes, long ElapsedMilliseconds);
public sealed record PdfContentResourceUsage(long PeakMemoryBytes, long CpuMilliseconds, long WallMilliseconds,
    long MemoryLimitBytes, long CpuLimitMilliseconds, long MaxInputBytes, long MaxOutputBytes, long MaxReportBytes, long MaxPixels);
public sealed record PdfContentEvidence(string EngineBinding, string ReceiptSha256, PdfContentReceipt Receipt,
    PdfContentResourceUsage Resources);

public interface IPdfContentEngine
{
    Task<PdfContentCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<PdfContentInspection> InspectAsync(DocumentVersion source, CancellationToken cancellationToken = default);
    Task<PdfContentEvidence> ExecuteAsync(DocumentVersion source, string outputPath, PdfContentParameters parameters,
        PdfContentInspection inspection, TimeSpan timeout, long maxOutputBytes, CancellationToken cancellationToken = default);
    Task VerifyEvidenceAsync(DocumentVersion source, string outputPath, PdfContentParameters parameters,
        PdfContentInspection inspection, PdfContentEvidence evidence, CancellationToken cancellationToken = default);
}
