using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Negative page-existence evidence requires a complete, internally consistent tree.
internal sealed class PdfPigPackagePageTreeReader(Func<IToken?, IToken?> resolve, PackageReadLimits limits, CancellationToken token)
{
    private readonly HashSet<IndirectReference> _seen = [];
    private int _work = limits.MaxPdfObjects;
    private int _pages;

    internal int? Read(DictionaryToken catalog, int parsedPages)
    {
        try
        {
            if (Get(catalog, "Pages") is not IndirectReferenceToken root ||
                Walk(root, null, 0) is not { } count || count < 1 || count != parsedPages || count != _pages) return null;
            return count;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PackageInputChangedException))
        {
            return null;
        }
    }

    private int? Walk(IndirectReferenceToken reference, IndirectReferenceToken? parent, int depth)
    {
        Tick(); if (depth > limits.MaxPdfDepth || !_seen.Add(reference.Data) || Resolve(reference) is not DictionaryToken node) return null;
        var owner = Get(node, "Parent");
        if (parent is null ? owner is not null : owner is not IndirectReferenceToken back || back.Data != parent.Data) return null;
        var type = (Resolve(Get(node, "Type")) as NameToken)?.Data;
        if (type == "Page")
        {
            if (parent is null || Get(node, "Kids") is not null || Get(node, "Count") is not null || ++_pages > limits.MaxPdfPages) return null;
            return 1;
        }
        if (type != "Pages" || Resolve(Get(node, "Kids")) is not ArrayToken children ||
            Resolve(Get(node, "Count")) is not NumericToken { HasDecimalPlaces: false } declared || declared.Long < 0 || declared.Long > limits.MaxPdfPages) return null;
        var total = 0;
        foreach (var child in children.Data)
        {
            Tick();
            if (child is not IndirectReferenceToken childReference || Walk(childReference, reference, depth + 1) is not { } count) return null;
            total += count; if (total > limits.MaxPdfPages) return null;
        }
        return total == declared.Long ? total : null;
    }

    private void Tick() { token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new PdfInspectionLimitException("Page-tree evidence budget exceeded."); }
    private IToken? Resolve(IToken? value) { Tick(); return resolve(value); }
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var value) ? value : null;
}
