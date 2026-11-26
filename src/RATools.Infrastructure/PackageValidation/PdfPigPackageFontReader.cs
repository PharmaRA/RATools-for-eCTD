using RATools.Application.PackageValidation;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Graphics.Operations.TextShowing;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Deliberately no "fully embedded" classification: a stream key is not proof of
// a valid, complete font program. Nested/implicit font state remains unqualified.
internal sealed class PdfPigPackageFontReader(PdfDocument document, Func<IToken?, IToken?> resolve, Action<int> count,
    Func<bool> qualifyPageTree)
{
    private bool? _qualifiedPageTree;
    internal IReadOnlyList<PdfFontObservation> Read()
    {
        var result = new List<PdfFontObservation>();
        foreach (var page in document.GetPages())
        {
            count(0);
            var operations = page.Operations;
            var unsafeResources = false;
            foreach (var operation in operations)
            {
                count(0);
                if (operation.Operator is "Do" or "gs") unsafeResources = true;
            }
            // Do may use a different resource namespace; gs may select a font.
            if (unsafeResources) continue;
            if (ReadResources(page.Dictionary) is not { } resources ||
                ResolveOptional(Get(resources, "Font")) is not DictionaryToken fonts) continue;
            string? selected = null;
            var stack = new Stack<string?>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in operations)
            {
                count(stack.Count);
                if (operation.Operator == "q") { stack.Push(selected); continue; }
                if (operation.Operator == "Q") { selected = stack.TryPop(out var restored) ? restored : null; continue; }
                if (operation is SetFontAndSize set) { selected = set.Font.Data; continue; }
                var showsText = operation switch
                {
                    ShowText text => text.Text?.Length > 0 || text.Bytes.Length > 0,
                    MoveToNextLineShowText text => text.Text?.Length > 0 || text.Bytes.Length > 0,
                    MoveToNextLineShowTextWithSpacing text => text.Text?.Length > 0 || text.Bytes.Length > 0,
                    ShowTextsWithPositioning text => HasText(text.Array),
                    _ => false
                };
                if (!showsText || selected is null || !seen.Add(selected)) continue;
                var path = $"page[{page.Number}]/Resources/Font/{selected}";
                if (ResolveOptional(Get(fonts, selected)) is not DictionaryToken font)
                { result.Add(new(path, null, null, PdfFontEmbeddingStatus.Unknown)); continue; }
                var name = (ResolveOptional(Get(font, "BaseFont")) as NameToken)?.Data;
                var subtype = (ResolveOptional(Get(font, "Subtype")) as NameToken)?.Data;
                var state = PdfFontEmbeddingStatus.Unknown;
                // Other types (including Type3 and composite fonts) need their own qualification.
                if (subtype == "Type1" && !string.IsNullOrWhiteSpace(name))
                {
                    if (!font.ContainsKey(NameToken.Create("FontDescriptor"))) state = PdfFontEmbeddingStatus.Missing;
                    else if (ResolveOptional(Get(font, "FontDescriptor")) is DictionaryToken descriptor &&
                        (ResolveOptional(Get(descriptor, "FontName")) as NameToken)?.Data == name &&
                        !descriptor.ContainsKey(NameToken.Create("FontFile")) &&
                        !descriptor.ContainsKey(NameToken.Create("FontFile2")) &&
                        !descriptor.ContainsKey(NameToken.Create("FontFile3"))) state = PdfFontEmbeddingStatus.Missing;
                }
                result.Add(new(path, name, subtype, state));
            }
        }
        return result;
    }

    // PDF Reference 1.7 Table 3.27 and pp. 149/153: inherit the whole dictionary
    // only when Resources is absent. Explicit empty or unresolved entries do not
    // authorize merging or falling back to an ancestor's font namespace.
    internal DictionaryToken? ReadResources(DictionaryToken page)
    {
        if (page.ContainsKey(NameToken.Create("Resources"))) return ResolveOptional(Get(page, "Resources")) as DictionaryToken;
        if (!(_qualifiedPageTree ??= qualifyPageTree())) return null;
        var seen = new HashSet<IndirectReference>();
        var node = page;
        while (Get(node, "Parent") is IndirectReferenceToken parent)
        {
            count(seen.Count + 1);
            if (!seen.Add(parent.Data) || ResolveOptional(parent) is not DictionaryToken ancestor ||
                (ResolveOptional(Get(ancestor, "Type")) as NameToken)?.Data != "Pages") return null;
            if (ancestor.ContainsKey(NameToken.Create("Resources"))) return ResolveOptional(Get(ancestor, "Resources")) as DictionaryToken;
            node = ancestor;
        }
        return null;
    }

    private bool HasText(IReadOnlyList<IToken> tokens)
    {
        var any = false;
        foreach (var token in tokens)
        {
            count(0);
            if (token is StringToken { Data.Length: > 0 } or HexToken { Data.Length: > 0 }) any = true;
        }
        return any;
    }
    private IToken? ResolveOptional(IToken? token)
    {
        try { return resolve(token); }
        catch (Exception exception) when (exception is not (OperationCanceledException or PdfInspectionLimitException))
        { return null; }
    }
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var token) ? token : null;
}
