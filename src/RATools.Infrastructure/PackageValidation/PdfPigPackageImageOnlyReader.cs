using RATools.Application.PackageValidation;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Narrow negative-text evidence, not OCR or proof of scanning provenance.
// Only complete unfiltered page streams in a known grammar can qualify.
internal sealed class PdfPigPackageImageOnlyReader(PdfDocument document, Func<IToken?, IToken?> resolve,
    PackageReadLimits limits, CancellationToken token)
{
    private int _work = limits.MaxPdfObjects;
    private long _bytes = Math.Min(limits.MaxFileBytes, 1024 * 1024);

    internal PdfImageOnlyStatus Read()
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var catalog = document.Structure.Catalog.CatalogDictionary;
            if (HasAny(catalog, "AcroForm", "StructTreeRoot", "OCProperties", "OpenAction", "AA", "Collection")) return PdfImageOnlyStatus.Unknown;
            var pages = 0;
            foreach (var page in document.GetPages())
            {
                Tick();
                if (++pages > limits.MaxPdfPages || page.Letters.Count != 0 || !string.IsNullOrWhiteSpace(page.Text) ||
                    HasAny(page.Dictionary, "Annots", "AA", "Metadata", "PieceInfo", "PresSteps") ||
                    Resolve(Get(page.Dictionary, "Resources")) is not DictionaryToken resources || HasAny(resources, "Properties", "ExtGState") ||
                    Resolve(Get(resources, "XObject")) is not DictionaryToken objects ||
                    ReadContent(Get(page.Dictionary, "Contents")) is not { } content) return PdfImageOnlyStatus.Unknown;
                var scanner = new CoreTokenScanner(new MemoryInputBytes(content), false,
                    new StackDepthGuard(limits.MaxPdfDepth), useLenientParsing: false);
                var operands = new List<IToken>(); var depth = 0; var images = 0;
                while (scanner.MoveNext())
                {
                    Tick();
                    var current = scanner.CurrentToken;
                    if (current is CommentToken) continue;
                    if (current is NumericToken or NameToken)
                    {
                        if (operands.Count >= 6) return PdfImageOnlyStatus.Unknown;
                        operands.Add(current); continue;
                    }
                    if (current is not OperatorToken operation) return PdfImageOnlyStatus.Unknown;
                    switch (operation.Data)
                    {
                        case "q":
                            if (operands.Count != 0 || ++depth > limits.MaxPdfDepth) return PdfImageOnlyStatus.Unknown;
                            break;
                        case "Q":
                            if (operands.Count != 0 || --depth < 0) return PdfImageOnlyStatus.Unknown;
                            break;
                        case "cm":
                            if (operands.Count != 6 || operands.Any(value => value is not NumericToken n || !double.IsFinite(n.Double))) return PdfImageOnlyStatus.Unknown;
                            var matrix = operands.Cast<NumericToken>().Select(value => value.Double).ToArray();
                            var determinant = matrix[0] * matrix[3] - matrix[1] * matrix[2];
                            if (!double.IsFinite(determinant) || determinant == 0) return PdfImageOnlyStatus.Unknown;
                            break;
                        case "Do":
                            if (operands.Count != 1 || operands[0] is not NameToken name ||
                                Resolve(Get(objects, name.Data)) is not StreamToken image || !Image(image)) return PdfImageOnlyStatus.Unknown;
                            images++;
                            break;
                        default: return PdfImageOnlyStatus.Unknown;
                    }
                    operands.Clear();
                }
                if (operands.Count != 0 || depth != 0 || images == 0 || page.NumberOfImages != images) return PdfImageOnlyStatus.Unknown;
            }
            token.ThrowIfCancellationRequested();
            return pages > 0 && pages == document.NumberOfPages ? PdfImageOnlyStatus.QualifiedImageOnly : PdfImageOnlyStatus.Unknown;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PackageInputChangedException))
        {
            // Bounded supplementary failures are unknown, never text absence or 3102 corruption.
            token.ThrowIfCancellationRequested();
            return PdfImageOnlyStatus.Unknown;
        }
    }

    private ReadOnlyMemory<byte>? ReadContent(IToken? value)
    {
        var resolved = Resolve(value);
        if (resolved is StreamToken single)
        {
            if (!PlainStream(single) || single.Data.Length > _bytes) return null;
            _bytes -= single.Data.Length;
            return single.Data;
        }
        if (resolved is not ArrayToken array || array.Length == 0) return null;
        var parts = new List<ReadOnlyMemory<byte>>();
        var total = 0;
        foreach (var member in array.Data)
        {
            Tick();
            if (Resolve(member) is not StreamToken stream || !PlainStream(stream) || stream.Data.Length > _bytes) return null;
            _bytes -= stream.Data.Length;
            if (stream.Data.Length == 0) continue;
            // PDF Reference Table 3.27 concatenates Contents in order. Qualify
            // only explicit line boundaries, so a token or comment cannot be
            // joined differently from the parser's stream-boundary handling.
            if (parts.Count > 0 && parts[^1].Span[^1] is not (10 or 13)) return null;
            total += stream.Data.Length;
            parts.Add(stream.Data);
        }
        if (parts.Count == 0) return null;
        var joined = new byte[total]; var offset = 0;
        foreach (var part in parts)
        {
            Tick(); part.Span.CopyTo(joined.AsSpan(offset)); offset += part.Length;
        }
        return joined;
    }

    private bool Image(StreamToken stream)
    {
        Tick(); var dictionary = stream.StreamDictionary;
        if (!PlainStream(stream) || HasAny(dictionary, "SMask", "Mask", "ImageMask", "Alternates", "OPI", "OC", "Metadata", "Decode") ||
            (Resolve(Get(dictionary, "Type")) as NameToken)?.Data != "XObject" ||
            (Resolve(Get(dictionary, "Subtype")) as NameToken)?.Data != "Image" ||
            Resolve(Get(dictionary, "Width")) is not NumericToken { HasDecimalPlaces: false } width || width.Long is < 1 or > 65535 ||
            Resolve(Get(dictionary, "Height")) is not NumericToken { HasDecimalPlaces: false } height || height.Long is < 1 or > 65535 ||
            Resolve(Get(dictionary, "BitsPerComponent")) is not NumericToken { HasDecimalPlaces: false, Long: 8 }) return false;
        var components = (Resolve(Get(dictionary, "ColorSpace")) as NameToken)?.Data switch { "DeviceGray" => 1, "DeviceRGB" => 3, _ => 0 };
        return components > 0 && width.Long * height.Long * components == stream.Data.Length;
    }

    private bool PlainStream(StreamToken stream) => !HasAny(stream.StreamDictionary, "Filter", "DecodeParms", "F", "FFilter", "FDecodeParms") &&
        Resolve(Get(stream.StreamDictionary, "Length")) is NumericToken { HasDecimalPlaces: false } length && length.Long == stream.Data.Length;
    private IToken? Resolve(IToken? value) { Tick(); return resolve(value); }
    private bool HasAny(DictionaryToken dictionary, params string[] names)
    {
        foreach (var name in names) { Tick(); if (dictionary.ContainsKey(NameToken.Create(name))) return true; }
        return false;
    }
    private void Tick() { token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new PdfInspectionLimitException("Image-only observation budget exceeded."); }
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var value) ? value : null;
}
