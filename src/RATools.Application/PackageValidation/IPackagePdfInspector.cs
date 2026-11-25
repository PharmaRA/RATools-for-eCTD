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

public sealed class PackagePdfFacts
{
    public PackagePdfFacts(PdfInspectionResult basics, IEnumerable<PdfNavigationLink> links,
        IEnumerable<PdfNamedDestination> destinations, bool navigationComplete, bool destinationsComplete,
        IEnumerable<string>? incompleteReasons = null, PackagePdfFormFacts? form = null,
        PdfContentProcessingStatus contentProcessing = PdfContentProcessingStatus.Unknown,
        IEnumerable<PdfFontObservation>? fonts = null)
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
}

public sealed class PdfInspectionLimitException(string message) : Exception(message);
