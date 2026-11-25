using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

internal static class PdfPigPackageLinearizationReader
{
    // Adobe PDF Reference 1.7 F.2.2: the first body object must be the
    // linearization dictionary, entirely within the first 1024 bytes.
    // This is a necessary-condition check, never a linearization validator.
    internal static PdfLinearizationStatus Read(Stream stream, PackageReadLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (!stream.CanSeek) return PdfLinearizationStatus.Unknown;
            var saved = stream.Position;
            try
            {
                stream.Position = 0;
                var bytes = new byte[(int)Math.Min(1024, stream.Length)];
                var used = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                token.ThrowIfCancellationRequested();
                if (used < 9 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) || bytes[5] is < (byte)'0' or > (byte)'9' || bytes[6] != '.' ||
                    bytes[7] is < (byte)'0' or > (byte)'9' || bytes[8] is not ((byte)'\r' or (byte)'\n')) return PdfLinearizationStatus.Unknown;
                var input = new MemoryInputBytes(bytes.AsMemory(0, used));
                var scanner = new CoreTokenScanner(input, false, new StackDepthGuard(limits.MaxPdfDepth), useLenientParsing: false);
                var work = Math.Min(limits.MaxPdfObjects, 64);
                if (Next() is not NumericToken number || number.HasDecimalPlaces || number.Long <= 0 ||
                    Next() is not NumericToken generation || generation.HasDecimalPlaces || generation.Long is < 0 or > 65535 ||
                    Next() is not OperatorToken { Data: "obj" } || Next() is not DictionaryToken dictionary ||
                    Next() is not OperatorToken { Data: "endobj" }) return PdfLinearizationStatus.Unknown;
                // A token cut at the window boundary must not look complete merely
                // because the scanner reached EOF. Require its real delimiter.
                var end = scanner.CurrentTokenStart + 6;
                if (end >= used || !bytes.AsSpan((int)scanner.CurrentTokenStart, 6).SequenceEqual("endobj"u8) ||
                    bytes[(int)end] is not (0 or 9 or 10 or 12 or 13 or 32 or (byte)'%')) return PdfLinearizationStatus.Unknown;
                token.ThrowIfCancellationRequested();
                return dictionary.ContainsKey(NameToken.Create("Linearized"))
                    ? PdfLinearizationStatus.DeclaredUnverified : PdfLinearizationStatus.MissingFirstObjectDeclaration;

                IToken? Next()
                {
                    while (work-- > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!scanner.MoveNext()) return null;
                        if (scanner.CurrentToken is not CommentToken) return scanner.CurrentToken;
                    }
                    return null;
                }
            }
            finally { stream.Position = saved; }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PackageInputChangedException))
        {
            // Failure of this bounded supplementary inspection is unknown evidence,
            // not a new content corruption classification for FDA 3102.
            return PdfLinearizationStatus.Unknown;
        }
    }
}
