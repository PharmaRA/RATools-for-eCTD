using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Independent page-Link identity, not a classification inferred from a generic link label.
internal sealed class PdfPigPackageHyperlinkReader(Func<IToken?, IToken?> resolve, PackageReadLimits limits, CancellationToken token)
{
    private int _work = limits.MaxPdfObjects;
    internal IReadOnlyList<PdfHyperlinkActionCount> MultipleActions { get; private set; } = [];
    internal IReadOnlyList<PdfExternalHyperlink> ExternalHyperlinks { get; private set; } = [];

    internal IReadOnlyList<PdfHyperlinkFileTarget> Read(IReadOnlyList<DictionaryToken> pages,
        IReadOnlyDictionary<IndirectReference, int> pageNumbers, int? qualifiedPageCount)
    {
        var candidates = new List<(IndirectReference Reference, PdfHyperlinkFileTarget Target)>();
        var actions = new List<(IndirectReference Reference, PdfHyperlinkActionCount Count)>();
        var external = new List<(IndirectReference Reference, PdfExternalHyperlink Link)>();
        var references = new Dictionary<IndirectReference, int>();
        MultipleActions = [];
        ExternalHyperlinks = [];
        try
        {
            if (qualifiedPageCount is null or < 1 || qualifiedPageCount != pages.Count || pageNumbers.Count != pages.Count) return [];
            var entries = 0;
            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                Tick(); var page = pages[pageIndex];
                if (Get(page, "Annots") is not { } rawAnnotations) continue;
                if (Resolve(rawAnnotations) is not ArrayToken annotations) return [];
                for (var index = 0; index < annotations.Length; index++)
                {
                    Tick(); if (++entries > limits.MaxPdfLinks) return [];
                    if (annotations.Data[index] is not IndirectReferenceToken reference) continue;
                    references[reference.Data] = references.GetValueOrDefault(reference.Data) + 1;
                    try
                    {
                        if (Resolve(reference) is not DictionaryToken annotation || Name(Get(annotation, "Subtype")) != "Link" ||
                            Has(annotation, "Type") && Name(Get(annotation, "Type")) != "Annot" ||
                            HasAny(annotation, "Dest", "AA", "QuadPoints", "OC", "AP")) continue;
                        if (Get(annotation, "F") is { } flags && Resolve(flags) is not NumericToken { HasDecimalPlaces: false, Long: 0 }) continue;
                        if (Get(annotation, "P") is { } owner &&
                            (owner is not IndirectReferenceToken ownerReference || !pageNumbers.TryGetValue(ownerReference.Data, out var number) || number != pageIndex + 1)) continue;
                        if (Resolve(Get(annotation, "Rect")) is not ArrayToken { Length: 4 } rectangle) continue;
                        var coordinates = new double[4]; var valid = true;
                        for (var coordinate = 0; coordinate < 4; coordinate++)
                        {
                            if (Resolve(rectangle.Data[coordinate]) is not NumericToken value || !double.IsFinite(value.Double)) { valid = false; break; }
                            coordinates[coordinate] = value.Double;
                        }
                        if (!valid || coordinates[2] <= coordinates[0] || coordinates[3] <= coordinates[1]) continue;
                        if (new PdfPigPackageBookmarkActionReader(Resolve, limits, token).Read(annotation) is int count && count > 1)
                            actions.Add((reference.Data, new($"page[{pageIndex + 1}]/annotation[{index + 1}]/A", pageIndex + 1, count)));
                        var kind = ReadExternal(annotation);
                        if (kind is PdfExternalHyperlinkKind.Web or PdfExternalHyperlinkKind.Email)
                            external.Add((reference.Data, new($"page[{pageIndex + 1}]/annotation[{index + 1}]/A/URI", pageIndex + 1, kind)));
                        if (Resolve(Get(annotation, "A")) is not DictionaryToken action || Name(Get(action, "S")) != "GoToR" ||
                            Has(action, "Next") || Has(action, "Type") && Name(Get(action, "Type")) != "Action") continue;
                        var destination = Resolve(Get(action, "D"));
                        long? pageTarget = null;
                        if (destination is ArrayToken array)
                        {
                            if (array.Length != 2 || Resolve(array.Data[0]) is not NumericToken { HasDecimalPlaces: false } targetPage || targetPage.Long < 0 || Name(array.Data[1]) != "Fit") continue;
                            pageTarget = targetPage.Long;
                        }
                        else if (string.IsNullOrEmpty(Text(destination)) && destination is not NameToken { Data.Length: > 0 }) continue;
                        var file = Resolve(Get(action, "F")); string? specification;
                        if (file is DictionaryToken dictionary)
                        {
                            if (HasAny(dictionary, "FS", "EF", "RF", "DOS", "Mac", "Unix", "ID", "V") ||
                                Has(dictionary, "Type") && Name(Get(dictionary, "Type")) != "Filespec") continue;
                            specification = Text(Resolve(Get(dictionary, "F")));
                            if (Get(dictionary, "UF") is { } unicode && Text(Resolve(unicode)) != specification) continue;
                        }
                        else specification = Text(file);
                        if (string.IsNullOrWhiteSpace(specification) || specification.Any(character => character is < ' ' or > '~')) continue;
                        candidates.Add((reference.Data, new($"page[{pageIndex + 1}]/annotation[{index + 1}]/A/F", pageIndex + 1, specification, pageTarget)));
                    }
                    catch (Exception exception) when (exception is not (OperationCanceledException or PdfInspectionLimitException or PackageInputChangedException))
                    {
                        // One unresolved annotation cannot establish a target, or erase another known annotation.
                    }
                }
            }
            var files = candidates.Where(candidate => references[candidate.Reference] == 1).Select(candidate => candidate.Target).ToArray();
            MultipleActions = actions.Where(action => references[action.Reference] == 1).Select(action => action.Count).ToArray();
            ExternalHyperlinks = external.Where(link => references[link.Reference] == 1).Select(link => link.Link).ToArray();
            return files;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PackageInputChangedException))
        {
            // Incomplete inventory cannot establish unique annotation ownership.
            return [];
        }
    }

    private PdfExternalHyperlinkKind ReadExternal(DictionaryToken annotation)
    {
        if (Resolve(Get(annotation, "A")) is not DictionaryToken action || Name(Get(action, "S")) != "URI" ||
            HasAny(action, "Next", "IsMap") || Has(action, "Type") && Name(Get(action, "Type")) != "Action") return PdfExternalHyperlinkKind.Unknown;
        var text = Text(Resolve(Get(action, "URI")));
        if (string.IsNullOrEmpty(text) || text.Any(character => character is <= ' ' or > '~') ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()) return PdfExternalHyperlinkKind.Unknown;
        // Classification only: no network access or address retained in regulatory evidence.
        return uri.Scheme switch
        {
            "http" or "https" when uri.Host.Length > 0 => PdfExternalHyperlinkKind.Web,
            "mailto" when uri.Host.Length > 0 => PdfExternalHyperlinkKind.Email,
            _ => PdfExternalHyperlinkKind.Unknown
        };
    }

    private void Tick() { token.ThrowIfCancellationRequested(); if (_work-- <= 0) throw new PdfInspectionLimitException("Hyperlink identity budget exceeded."); }
    private IToken? Resolve(IToken? value) { Tick(); return resolve(value); }
    private string? Name(IToken? value) => (Resolve(value) as NameToken)?.Data;
    private static string? Text(IToken? value) => value switch { StringToken text => text.Data, HexToken text => text.Data, _ => null };
    private static bool Has(DictionaryToken dictionary, string name) => dictionary.ContainsKey(NameToken.Create(name));
    private static bool HasAny(DictionaryToken dictionary, params string[] names) => names.Any(name => Has(dictionary, name));
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var value) ? value : null;
}
