using RATools.Application.PackageValidation;
using RATools.Infrastructure.Publishing.Validation.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

public sealed class PdfPigPackageInspector : IPackagePdfInspector
{
    public string EngineVersion => "pdfpig-0.1.15/package-navigation-v1";
    public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Length > limits.MaxFileBytes) throw new PdfInspectionLimitException("PDF file byte limit exceeded.");
        try
        {
            using var guarded = new CancellationStream(stream, cancellationToken);
            using var document = PdfDocument.Open(guarded, new ParsingOptions { UseLenientParsing = false, MaxStackDepth = limits.MaxPdfDepth });
            if (document.NumberOfPages > limits.MaxPdfPages) throw new PdfInspectionLimitException("PDF page limit exceeded.");
            var navigation = new Navigation(document, limits, cancellationToken);
            navigation.Read();
            cancellationToken.ThrowIfCancellationRequested();
            var basics = PdfPigPdfInspector.InspectDocument(document, cancellationToken, inspectBookmarks: false) with
            {
                HasBookmarks = navigation.BookmarkCount > 0,
                BookmarkMaxDepth = navigation.NavigationComplete ? navigation.BookmarkDepth : null
            };
            cancellationToken.ThrowIfCancellationRequested();
            return new(basics, navigation.Links, navigation.Destinations, navigation.NavigationComplete,
                navigation.DestinationsComplete, navigation.Reasons);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PdfInspectionLimitException))
        {
            return new(PdfPigPdfInspector.ParseFailure(exception.Message), [], [], false, false, ["PDF parsing did not complete."]);
        }
    }

    // Read original destinations/actions: GetHyperlinks().Uri alone silently loses GoTo/GoToR,
    // invalid destinations, named targets and bookmark actions.
    private sealed class Navigation(PdfDocument document, PackageReadLimits limits, CancellationToken cancellationToken)
    {
        public List<PdfNavigationLink> Links { get; } = [];
        public List<PdfNamedDestination> Destinations { get; } = [];
        public List<string> Reasons { get; } = [];
        public bool NavigationComplete { get; private set; } = true;
        public bool DestinationsComplete { get; private set; } = true;
        public int BookmarkCount { get; private set; }
        public int BookmarkDepth { get; private set; }
        private readonly Dictionary<IndirectReference, int> _pageNumbers = [];
        private readonly List<DictionaryToken> _pages = [];
        private readonly HashSet<DictionaryToken> _pageTree = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<DictionaryToken> _nameTree = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<DictionaryToken> _outlines = new(ReferenceEqualityComparer.Instance);
        private int _objects;
        private bool _uriBase;

        public void Read()
        {
            var catalog = document.Structure.Catalog.CatalogDictionary;
            _uriBase = Dictionary(Get(catalog, "URI")) is { } uri && Get(uri, "Base") is not null;
            ReadPages(Get(catalog, "Pages"), 0);
            if (_pages.Count != document.NumberOfPages) Unknown("Page tree count differs from parsed page count.", destinations: true);
            if (Get(catalog, "Dests") is { } oldDestinations)
            {
                if (Dictionary(oldDestinations) is not { } old) Unknown("Invalid catalog Dests dictionary.", destinations: true);
                else foreach (var (name, target) in old.Data) AddDestination(name, target);
            }
            if (Get(catalog, "Names") is { } namesToken)
            {
                if (Dictionary(namesToken) is not { } names) Unknown("Invalid catalog Names dictionary.", destinations: true);
                else if (Get(names, "Dests") is { } tree) ReadNames(tree, 0);
            }
            for (var index = 0; index < _pages.Count; index++)
            {
                Count(0);
                var page = _pages[index];
                if (Get(page, "Annots") is { } annots)
                {
                    if (Resolve(annots) is not ArrayToken array) Unknown("Invalid annotation array.");
                    else for (var annotationIndex = 0; annotationIndex < array.Length; annotationIndex++)
                    {
                        var annotationToken = array.Data[annotationIndex];
                        if (Dictionary(annotationToken) is not { } annotation) { Unknown("Unresolved annotation dictionary."); continue; }
                        var link = Name(Get(annotation, "Subtype")) == "Link";
                        if (link || Get(annotation, "A") is not null || Get(annotation, "Dest") is not null)
                            ReadAction(annotation, $"page[{index + 1}]/annotation[{annotationIndex + 1}]", index + 1, required: link);
                        if (Get(annotation, "AA") is not null) Unknown("Annotation additional actions require further inspection.");
                    }
                }
                if (Get(page, "AA") is not null) Unknown("Page additional actions require further inspection.");
            }
            if (Get(catalog, "Outlines") is { } outlineToken)
            {
                if (Dictionary(outlineToken) is not { } outlines) Unknown("Invalid outline dictionary.");
                else if (Get(outlines, "First") is { } first) ReadOutlines(first, 0);
            }
            if (Get(catalog, "OpenAction") is { } open)
            {
                var value = Resolve(open);
                if (value is DictionaryToken action) ReadAction(new DictionaryToken(new Dictionary<NameToken, IToken> { [NameToken.Create("A")] = action }), "catalog/OpenAction", null, true);
                else AddLink("catalog/OpenAction", null, Destination(value, remote: false));
            }
            if (Get(catalog, "AA") is not null) Unknown("Catalog additional actions require further inspection.");
        }

        private void ReadPages(IToken? token, int depth)
        {
            Count(depth);
            var raw = token;
            if (Dictionary(token) is not { } node || !_pageTree.Add(node)) { Unknown("Invalid or repeated page tree node.", destinations: true); return; }
            if (Name(Get(node, "Type")) == "Page")
            {
                if (_pages.Count >= limits.MaxPdfPages) throw new PdfInspectionLimitException("PDF page tree limit exceeded.");
                _pages.Add(node);
                if (raw is IndirectReferenceToken reference) _pageNumbers.TryAdd(reference.Data, _pages.Count);
                return;
            }
            if (Name(Get(node, "Type")) != "Pages" || Resolve(Get(node, "Kids")) is not ArrayToken children)
            { Unknown("Invalid page tree branch.", destinations: true); return; }
            foreach (var child in children.Data) ReadPages(child, depth + 1);
        }

        private void ReadNames(IToken token, int depth)
        {
            Count(depth);
            if (Dictionary(token) is not { } node || !_nameTree.Add(node)) { Unknown("Invalid or repeated destination name-tree node.", destinations: true); return; }
            if (Get(node, "Names") is { } entries)
            {
                if (Resolve(entries) is not ArrayToken array || array.Length % 2 != 0) Unknown("Malformed destination name pairs.", destinations: true);
                else for (var index = 0; index < array.Length; index += 2)
                {
                    var name = Text(Resolve(array.Data[index]));
                    if (name is null) Unknown("Invalid destination name.", destinations: true);
                    else AddDestination(name, array.Data[index + 1]);
                }
            }
            if (Get(node, "Kids") is { } kids)
            {
                if (Resolve(kids) is not ArrayToken array) Unknown("Invalid destination name-tree children.", destinations: true);
                else foreach (var child in array.Data) ReadNames(child, depth + 1);
            }
            if (Get(node, "Names") is null && Get(node, "Kids") is null) Unknown("Empty destination name-tree node.", destinations: true);
        }

        private void AddDestination(string name, IToken token)
        {
            Count(0);
            var resolved = Resolve(token);
            if (resolved is DictionaryToken dictionary) resolved = Resolve(Get(dictionary, "D"));
            var target = Destination(resolved, remote: false);
            if (target.NamedDestination is not null || target.UnsupportedReason is not null)
                Unknown("Named destination does not identify a supported explicit destination.", destinations: true);
            Destinations.Add(new(name, target.PageNumber, target.Invalid));
        }

        private void ReadOutlines(IToken first, int depth)
        {
            IToken? token = first;
            while (token is not null)
            {
                Count(depth);
                if (Dictionary(token) is not { } node || !_outlines.Add(node)) { Unknown("Invalid or repeated outline node."); return; }
                BookmarkCount++;
                BookmarkDepth = Math.Max(BookmarkDepth, depth + 1);
                ReadAction(node, $"outline[{BookmarkCount}]", null, false);
                if (Get(node, "First") is { } child) ReadOutlines(child, depth + 1);
                token = Get(node, "Next");
            }
        }

        private void ReadAction(DictionaryToken node, string path, int? sourcePage, bool required)
        {
            Count(0);
            var destination = Get(node, "Dest");
            var actionToken = Get(node, "A");
            if (destination is not null && actionToken is not null) Unknown("An object has both a Dest and an action.");
            if (destination is not null) AddLink(path, sourcePage, Destination(destination, remote: false));
            if (actionToken is null)
            {
                if (required && destination is null) AddLink(path, sourcePage, new(Invalid: true));
                return;
            }
            if (Dictionary(actionToken) is not { } action) { AddLink(path, sourcePage, new(Invalid: true)); return; }
            var kind = Name(Get(action, "S"));
            PdfDestinationTarget target;
            switch (kind)
            {
                case "URI":
                    var uri = Text(Resolve(Get(action, "URI")));
                    target = new(Uri: uri, Invalid: string.IsNullOrEmpty(uri), UnsupportedReason: _uriBase ? "Catalog URI Base changes relative URI semantics." : null);
                    break;
                case "GoTo": target = Destination(Get(action, "D"), remote: false); break;
                case "GoToR":
                    var fileToken = Resolve(Get(action, "F"));
                    var file = fileToken is DictionaryToken specification ? Text(Resolve(Get(specification, "UF") ?? Get(specification, "F"))) : Text(fileToken);
                    target = Destination(Get(action, "D"), remote: true) with { FileSpecification = file };
                    if (string.IsNullOrEmpty(file)) target = target with { Invalid = true };
                    break;
                default: target = new(UnsupportedReason: "Unsupported PDF action: " + (kind ?? "missing action type")); break;
            }
            AddLink(path + "/" + kind, sourcePage, target);
            if (Get(action, "Next") is not null) Unknown("Chained PDF actions require further inspection.");
        }

        private PdfDestinationTarget Destination(IToken? token, bool remote)
        {
            var resolved = Resolve(token);
            if (resolved is NameToken name) return new(NamedDestination: name.Data);
            if (Text(resolved) is { } text) return new(NamedDestination: text);
            if (resolved is not ArrayToken array || array.Length < 2) return new(Invalid: true);
            var type = Name(array.Data[1]);
            if (type is not ("XYZ" or "Fit" or "FitH" or "FitV" or "FitR" or "FitB" or "FitBH" or "FitBV"))
                return new(UnsupportedReason: "Unknown explicit destination type.");
            if (remote)
            {
                if (Resolve(array.Data[0]) is NumericToken number && !number.HasDecimalPlaces && number.Long is >= 0 and < int.MaxValue)
                    return new(PageNumber: (int)number.Long + 1); // GoToR uses a zero-based page index.
                return new(Invalid: true);
            }
            return array.Data[0] is IndirectReferenceToken reference && _pageNumbers.TryGetValue(reference.Data, out var page)
                ? new(PageNumber: page) : new(Invalid: true);
        }

        private void AddLink(string path, int? page, PdfDestinationTarget target)
        {
            if (Links.Count >= limits.MaxPdfLinks) throw new PdfInspectionLimitException("PDF link limit exceeded.");
            if (target.UnsupportedReason is not null) Unknown(target.UnsupportedReason);
            Links.Add(new(path, page, target));
        }

        private void Unknown(string reason, bool destinations = false)
        {
            NavigationComplete = false;
            if (destinations) DestinationsComplete = false;
            if (!Reasons.Contains(reason, StringComparer.Ordinal)) Reasons.Add(reason);
        }

        private void Count(int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_objects > limits.MaxPdfObjects || depth > limits.MaxPdfDepth)
                throw new PdfInspectionLimitException("PDF navigation object/depth limit exceeded.");
        }

        private IToken? Resolve(IToken? token)
        {
            var visited = new HashSet<IndirectReference>();
            while (token is IndirectReferenceToken reference)
            {
                Count(visited.Count);
                if (!visited.Add(reference.Data)) return null;
                token = document.Structure.GetObject(reference.Data)?.Data;
            }
            return token;
        }
        private DictionaryToken? Dictionary(IToken? token) => Resolve(token) as DictionaryToken;
        private string? Name(IToken? token) => (Resolve(token) as NameToken)?.Data;
        private static string? Text(IToken? token) => token switch { StringToken value => value.Data, HexToken value => value.Data, _ => null };
        private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var token) ? token : null;
    }

    private sealed class CancellationStream(Stream inner, CancellationToken cancellationToken) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { cancellationToken.ThrowIfCancellationRequested(); inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) { cancellationToken.ThrowIfCancellationRequested(); return inner.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { cancellationToken.ThrowIfCancellationRequested(); return inner.Read(buffer); }
        public override int ReadByte() { cancellationToken.ThrowIfCancellationRequested(); return inner.ReadByte(); }
        public override long Seek(long offset, SeekOrigin origin) { cancellationToken.ThrowIfCancellationRequested(); return inner.Seek(offset, origin); }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // The caller owns the captured stream.
    }
}
