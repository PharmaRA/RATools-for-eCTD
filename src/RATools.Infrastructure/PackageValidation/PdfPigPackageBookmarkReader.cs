using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Qualified outline identity is independent of generic navigation-link labels.
// All structural links must agree before any candidate is published.
internal sealed class PdfPigPackageBookmarkReader(Func<IToken?, IToken?> resolve, PackageReadLimits limits, CancellationToken token)
{
    private readonly HashSet<IndirectReference> _seen = [];
    private readonly List<PdfBookmarkFileTarget> _targets = [];
    private readonly List<PdfBookmarkActionCount> _actions = [];
    private readonly List<PdfExternalBookmark> _external = [];
    private readonly List<PdfInactiveBookmark> _inactive = [];
    private readonly List<PdfBookmarkZoom> _zooms = [];
    private bool _complete;
    internal IReadOnlyList<PdfBookmarkActionCount> MultipleActions => _complete ? _actions.ToArray() : [];
    internal IReadOnlyList<PdfExternalBookmark> ExternalBookmarks => _complete ? _external.ToArray() : [];
    internal IReadOnlyList<PdfInactiveBookmark> InactiveBookmarks => _complete ? _inactive.ToArray() : [];
    internal IReadOnlyList<PdfBookmarkZoom> NonInheritingZooms => _complete ? _zooms.ToArray() : [];
    private int _work = limits.MaxPdfObjects;
    private int _items;

    internal IReadOnlyList<PdfBookmarkFileTarget> Read(DictionaryToken catalog)
    {
        try
        {
            if (Get(catalog, "Outlines") is not IndirectReferenceToken rootReference ||
                Resolve(rootReference) is not DictionaryToken root || Name(Get(root, "Type")) != "Outlines") return [];
            _seen.Add(rootReference.Data);
            _complete = Children(root, rootReference, 0);
            return _complete ? _targets.ToArray() : [];
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PackageInputChangedException))
        {
            // Supplementary evidence failure is not document corruption or proof of absence.
            return [];
        }
    }

    private bool Children(DictionaryToken parent, IndirectReferenceToken parentReference, int depth)
    {
        Tick(); if (depth > limits.MaxPdfDepth) return false;
        var first = Get(parent, "First"); var last = Get(parent, "Last");
        if (first is null && last is null) return true;
        if (first is not IndirectReferenceToken current || last is not IndirectReferenceToken end) return false;
        IndirectReferenceToken? previous = null;
        while (true)
        {
            Tick();
            if (!_seen.Add(current.Data) || ++_items > limits.MaxPdfLinks || Resolve(current) is not DictionaryToken item ||
                Get(item, "Parent") is not IndirectReferenceToken owner || owner.Data != parentReference.Data ||
                Text(Resolve(Get(item, "Title"))) is null) return false;
            var prev = Get(item, "Prev");
            if (previous is null ? prev is not null : prev is not IndirectReferenceToken back || back.Data != previous.Data) return false;
            ReadAction(item, _items);
            ReadExternal(item, _items);
            ReadZoom(item, _items);
            // Key absence is not a failed resolution or an explicit null value.
            // Parent outline items may act as expand/collapse headings; leave them unknown.
            if (!HasAny(item, "A", "Dest", "AA", "SE", "First", "Last", "Count"))
                _inactive.Add(new($"outline[{_items}]"));
            if (new PdfPigPackageBookmarkActionReader(Resolve, limits, token).Read(item) is int count && count > 1)
                _actions.Add(new($"outline[{_items}]/A", count));
            if (!Children(item, current, depth + 1)) return false;
            var next = Get(item, "Next");
            if (next is null) return current.Data == end.Data;
            if (current.Data == end.Data || next is not IndirectReferenceToken forward) return false;
            previous = current; current = forward;
        }
    }

    private void ReadAction(DictionaryToken item, int number)
    {
        if (HasAny(item, "Dest", "AA") || Resolve(Get(item, "A")) is not DictionaryToken action ||
            Name(Get(action, "S")) != "GoToR" || HasAny(action, "Next") ||
            Get(action, "Type") is not null && Name(Get(action, "Type")) != "Action") return;
        var destination = Resolve(Get(action, "D"));
        long? pageIndex = null;
        // Only an explicit simple destination or nonempty named destination is qualified.
        if (destination is ArrayToken array)
        {
            if (array.Data.Count < 2 || Resolve(array.Data[0]) is not NumericToken { HasDecimalPlaces: false } page || page.Long < 0) return;
            var mode = Name(array.Data[1]);
            if (mode == "Fit" && array.Length == 2) pageIndex = page.Long;
            else if (mode == "XYZ" && array.Length == 5 && Coordinate(array.Data[2]) && Coordinate(array.Data[3]))
            {
                var zoom = Resolve(array.Data[4]);
                if (zoom is not NullToken && (zoom is not NumericToken factor || !double.IsFinite(factor.Double) || factor.Double < 0)) return;
                // GoToR XYZ uses the same nonnegative zero-based page index as
                // Fit; the consumer separately qualifies the target page tree.
                pageIndex = page.Long;
            }
            else return;
        }
        else if (string.IsNullOrEmpty(Text(destination)) && destination is not NameToken { Data.Length: > 0 }) return;
        var file = Resolve(Get(action, "F"));
        string? value;
        if (file is DictionaryToken specification)
        {
            if (HasAny(specification, "FS", "EF", "RF", "DOS", "Mac", "Unix", "ID", "V") ||
                Get(specification, "Type") is not null && Name(Get(specification, "Type")) != "Filespec") return;
            value = Text(Resolve(Get(specification, "F")));
            if (Get(specification, "UF") is { } unicode && Text(Resolve(unicode)) != value) return;
        }
        else value = Text(file);
        // Encoding/platform variants require separate qualification, not guessed conversion.
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character is < ' ' or > '~')) return;
        _targets.Add(new($"outline[{number}]/A/F", value, pageIndex));
    }

    private void ReadZoom(DictionaryToken item, int number)
    {
        if (HasAny(item, "Dest", "AA") || Resolve(Get(item, "A")) is not DictionaryToken action ||
            Name(Get(action, "S")) != "GoToR" || HasAny(action, "Next") ||
            Get(action, "Type") is not null && Name(Get(action, "Type")) != "Action") return;
        var file = Text(Resolve(Get(action, "F")));
        if (string.IsNullOrWhiteSpace(file) || file.Any(character => character is < ' ' or > '~') ||
            Resolve(Get(action, "D")) is not ArrayToken destination || destination.Length < 2 ||
            Resolve(destination.Data[0]) is not NumericToken { HasDecimalPlaces: false } page || page.Long < 0) return;
        var mode = Name(destination.Data[1]);
        var kind = PdfBookmarkZoomKind.Unknown;
        if (mode == "Fit" && destination.Length == 2) kind = PdfBookmarkZoomKind.FitPage;
        else if (mode == "XYZ" && destination.Length == 5 && Coordinate(destination.Data[2]) && Coordinate(destination.Data[3]) &&
            Resolve(destination.Data[4]) is NumericToken zoom && double.IsFinite(zoom.Double) && zoom.Double > 0)
            kind = PdfBookmarkZoomKind.FixedFactor;
        // PDF Reference 1.7 Table 8.2: XYZ zoom 0 or null retains the current factor.
        if (kind != PdfBookmarkZoomKind.Unknown) _zooms.Add(new($"outline[{number}]/A/D", kind));
    }

    private bool Coordinate(IToken value) => Resolve(value) switch
    {
        NullToken => true,
        NumericToken number => double.IsFinite(number.Double),
        _ => false
    };

    private void ReadExternal(DictionaryToken item, int number)
    {
        if (HasAny(item, "Dest", "AA") || Resolve(Get(item, "A")) is not DictionaryToken action ||
            Name(Get(action, "S")) != "URI" || HasAny(action, "Next", "IsMap") ||
            Get(action, "Type") is not null && Name(Get(action, "Type")) != "Action") return;
        var text = Text(Resolve(Get(action, "URI")));
        if (string.IsNullOrEmpty(text) || text.Any(character => character is <= ' ' or > '~') ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()) return;
        var kind = uri.Scheme switch
        {
            "http" or "https" when uri.Host.Length > 0 => PdfExternalBookmarkKind.Web,
            "mailto" when uri.Host.Length > 0 => PdfExternalBookmarkKind.Email,
            _ => PdfExternalBookmarkKind.Unknown
        };
        // Do not retain the address or query in the new regulatory evidence.
        if (kind != PdfExternalBookmarkKind.Unknown) _external.Add(new($"outline[{number}]/A/URI", kind));
    }

    private void Tick() { token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new PdfInspectionLimitException("Bookmark evidence budget exceeded."); }
    private IToken? Resolve(IToken? value) { Tick(); return resolve(value); }
    private string? Name(IToken? value) => (Resolve(value) as NameToken)?.Data;
    private static string? Text(IToken? value) => value switch { StringToken text => text.Data, HexToken text => text.Data, _ => null };
    private static bool HasAny(DictionaryToken dictionary, params string[] names) => names.Any(name => dictionary.ContainsKey(NameToken.Create(name)));
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var value) ? value : null;
}
