using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Counts complete supported action executions, never outline sibling links.
internal sealed class PdfPigPackageBookmarkActionReader(Func<IToken?, IToken?> resolve, PackageReadLimits limits, CancellationToken token)
{
    private readonly HashSet<IndirectReference> _activeReferences = [];
    private readonly HashSet<DictionaryToken> _activeDictionaries = new(ReferenceEqualityComparer.Instance);
    private int _work = limits.MaxPdfObjects;
    private int _count;

    internal int? Read(DictionaryToken item)
    {
        try
        {
            if (Has(item, "Dest") || Has(item, "AA") || Get(item, "A") is not { } action || !Walk(action, 0)) return null;
            return _count;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PackageInputChangedException))
        {
            return null;
        }
    }

    private bool Walk(IToken raw, int depth)
    {
        Tick(); if (depth > limits.MaxPdfDepth) return false;
        var reference = raw as IndirectReferenceToken;
        if (reference is not null && !_activeReferences.Add(reference.Data)) return false;
        DictionaryToken? action = null;
        try
        {
            if (Resolve(raw) is not DictionaryToken dictionary || !_activeDictionaries.Add(dictionary)) return false;
            action = dictionary;
            if (++_count > limits.MaxPdfLinks || Name(Get(action, "S")) != "GoToR" ||
                Has(action, "Type") && Name(Get(action, "Type")) != "Action") return false;
            var file = Text(Resolve(Get(action, "F")));
            if (string.IsNullOrWhiteSpace(file) || file.Any(character => character is < ' ' or > '~')) return false;
            var destination = Resolve(Get(action, "D"));
            if (destination is ArrayToken array)
            {
                if (array.Length != 2 || Resolve(array.Data[0]) is not NumericToken { HasDecimalPlaces: false } page || page.Long < 0 || Name(array.Data[1]) != "Fit") return false;
            }
            else if (string.IsNullOrEmpty(Text(destination)) && destination is not NameToken { Data.Length: > 0 }) return false;
            if (Get(action, "Next") is not { } next) return true;
            if (Resolve(next) is ArrayToken actions)
            {
                foreach (var child in actions.Data) { Tick(); if (!Walk(child, depth + 1)) return false; }
                return true;
            }
            return Walk(next, depth + 1);
        }
        finally
        {
            if (action is not null) _activeDictionaries.Remove(action);
            if (reference is not null) _activeReferences.Remove(reference.Data);
        }
    }

    private void Tick() { token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new PdfInspectionLimitException("Bookmark action evidence budget exceeded."); }
    private IToken? Resolve(IToken? value) { Tick(); return resolve(value); }
    private string? Name(IToken? value) => (Resolve(value) as NameToken)?.Data;
    private static string? Text(IToken? value) => value switch { StringToken text => text.Data, HexToken text => text.Data, _ => null };
    private static bool Has(DictionaryToken dictionary, string name) => dictionary.ContainsKey(NameToken.Create(name));
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var value) ? value : null;
}
