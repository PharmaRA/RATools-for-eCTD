using RATools.Application.Publishing.Validation.Pdf;

namespace RATools.Application.PackageValidation;

public interface IPackagePdfInspector
{
    string EngineVersion { get; }
    PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken);
}

public sealed record PdfDestinationTarget(string? FileSpecification = null, int? PageNumber = null,
    string? NamedDestination = null, string? Uri = null, bool Invalid = false, string? UnsupportedReason = null);

public sealed record PdfNavigationLink(string ObjectPath, int? SourcePageNumber, PdfDestinationTarget Target);
public sealed record PdfNamedDestination(string Name, int? PageNumber, bool Invalid = false);

public enum PdfContentProcessingStatus { Unknown, Processed, InvalidContent, Encrypted, ResourceLimited, InputUnavailable }
public enum PdfFontEmbeddingStatus { Unknown, Missing }
public sealed record PdfFontObservation(string ObjectPath, string? BaseFont, string? Subtype, PdfFontEmbeddingStatus Embedding);
public sealed record PdfVersionDeclaration(string HeaderVersion, bool HasCatalogVersion, bool HasExtensions, bool HasMetadata);
public enum PdfLinearizationStatus { Unknown, MissingFirstObjectDeclaration, DeclaredUnverified }
public sealed record PdfOpeningViewFacts(string? PageMode, bool? HasBookmarks, bool HasViewActions);
public sealed record PdfMarkupAnnotation(string ObjectPath, int PageNumber, string Subtype);
public enum PdfImageOnlyStatus { Unknown, QualifiedImageOnly }
public sealed record PdfBookmarkFileTarget(string ObjectPath, string FileSpecification, long? ZeroBasedPageIndex = null);
public sealed record PdfBookmarkActionCount(string ObjectPath, int Count);
public enum PdfExternalBookmarkKind { Unknown, Web, Email }
public sealed record PdfExternalBookmark(string ObjectPath, PdfExternalBookmarkKind Kind);
public sealed record PdfInactiveBookmark(string ObjectPath);
public enum PdfBookmarkZoomKind { Unknown, FitPage, FixedFactor }
public sealed record PdfBookmarkZoom(string ObjectPath, PdfBookmarkZoomKind Kind);
public sealed record PdfHyperlinkFileTarget(string ObjectPath, int PageNumber, string FileSpecification, long? ZeroBasedPageIndex = null);
public sealed record PdfHyperlinkActionCount(string ObjectPath, int PageNumber, int Count);
public enum PdfExternalHyperlinkKind { Unknown, Web, Email }
public sealed record PdfExternalHyperlink(string ObjectPath, int PageNumber, PdfExternalHyperlinkKind Kind);

public sealed class PackagePdfFacts
{
    public PackagePdfFacts(PdfInspectionResult basics, IEnumerable<PdfNavigationLink> links,
        IEnumerable<PdfNamedDestination> destinations, bool navigationComplete, bool destinationsComplete,
        IEnumerable<string>? incompleteReasons = null, PackagePdfFormFacts? form = null,
        PdfContentProcessingStatus contentProcessing = PdfContentProcessingStatus.Unknown,
        IEnumerable<PdfFontObservation>? fonts = null, PdfVersionDeclaration? versionDeclaration = null,
        PdfLinearizationStatus linearization = PdfLinearizationStatus.Unknown, PdfOpeningViewFacts? openingView = null,
        IEnumerable<PdfMarkupAnnotation>? markupAnnotations = null, PdfImageOnlyStatus imageOnly = PdfImageOnlyStatus.Unknown,
        IEnumerable<PdfBookmarkFileTarget>? bookmarkFiles = null, int? qualifiedPageCount = null,
        IEnumerable<PdfBookmarkActionCount>? multipleBookmarkActions = null,
        IEnumerable<PdfExternalBookmark>? externalBookmarks = null,
        IEnumerable<PdfInactiveBookmark>? inactiveBookmarks = null,
        IEnumerable<PdfBookmarkZoom>? nonInheritingBookmarkZooms = null,
        IEnumerable<PdfHyperlinkFileTarget>? hyperlinkFiles = null,
        IEnumerable<PdfHyperlinkActionCount>? multipleHyperlinkActions = null,
        IEnumerable<PdfExternalHyperlink>? externalHyperlinks = null)
    {
        Basics = basics with
        {
            NonEmbeddedFonts = Array.AsReadOnly(basics.NonEmbeddedFonts.ToArray()),
            Links = Array.AsReadOnly(basics.Links.ToArray())
        };
        Links = Array.AsReadOnly(links.ToArray());
        Destinations = Array.AsReadOnly(destinations.ToArray());
        NavigationComplete = navigationComplete;
        DestinationsComplete = destinationsComplete;
        IncompleteReasons = Array.AsReadOnly((incompleteReasons ?? []).ToArray());
        Form = form ?? PackagePdfFormFacts.Unknown;
        ContentProcessing = contentProcessing;
        Fonts = Array.AsReadOnly((fonts ?? []).ToArray());
        VersionDeclaration = versionDeclaration;
        Linearization = linearization;
        OpeningView = openingView;
        MarkupAnnotations = Array.AsReadOnly((markupAnnotations ?? []).ToArray());
        ImageOnly = imageOnly;
        BookmarkFiles = Array.AsReadOnly((bookmarkFiles ?? []).ToArray());
        QualifiedPageCount = qualifiedPageCount;
        MultipleBookmarkActions = Array.AsReadOnly((multipleBookmarkActions ?? []).ToArray());
        ExternalBookmarks = Array.AsReadOnly((externalBookmarks ?? []).ToArray());
        InactiveBookmarks = Array.AsReadOnly((inactiveBookmarks ?? []).ToArray());
        NonInheritingBookmarkZooms = Array.AsReadOnly((nonInheritingBookmarkZooms ?? []).ToArray());
        HyperlinkFiles = Array.AsReadOnly((hyperlinkFiles ?? []).ToArray());
        MultipleHyperlinkActions = Array.AsReadOnly((multipleHyperlinkActions ?? []).ToArray());
        ExternalHyperlinks = Array.AsReadOnly((externalHyperlinks ?? []).ToArray());
    }
    public PdfInspectionResult Basics { get; }
    public IReadOnlyList<PdfNavigationLink> Links { get; }
    public IReadOnlyList<PdfNamedDestination> Destinations { get; }
    public bool NavigationComplete { get; }
    public bool DestinationsComplete { get; }
    public IReadOnlyList<string> IncompleteReasons { get; }
    public PackagePdfFormFacts Form { get; }
    public PdfContentProcessingStatus ContentProcessing { get; }
    public IReadOnlyList<PdfFontObservation> Fonts { get; }
    public PdfVersionDeclaration? VersionDeclaration { get; }
    public PdfLinearizationStatus Linearization { get; }
    public PdfOpeningViewFacts? OpeningView { get; }
    public IReadOnlyList<PdfMarkupAnnotation> MarkupAnnotations { get; }
    public PdfImageOnlyStatus ImageOnly { get; }
    public IReadOnlyList<PdfBookmarkFileTarget> BookmarkFiles { get; }
    public int? QualifiedPageCount { get; }
    public IReadOnlyList<PdfBookmarkActionCount> MultipleBookmarkActions { get; }
    public IReadOnlyList<PdfExternalBookmark> ExternalBookmarks { get; }
    public IReadOnlyList<PdfInactiveBookmark> InactiveBookmarks { get; }
    public IReadOnlyList<PdfBookmarkZoom> NonInheritingBookmarkZooms { get; }
    public IReadOnlyList<PdfHyperlinkFileTarget> HyperlinkFiles { get; }
    public IReadOnlyList<PdfHyperlinkActionCount> MultipleHyperlinkActions { get; }
    public IReadOnlyList<PdfExternalHyperlink> ExternalHyperlinks { get; }
}

public sealed class PdfInspectionLimitException(string message) : Exception(message);
