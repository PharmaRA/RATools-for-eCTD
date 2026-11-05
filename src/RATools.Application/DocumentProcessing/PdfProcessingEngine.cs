namespace RATools.Application.DocumentProcessing;

public sealed record PdfEngineRequest(
    string SourcePath,
    string OutputPath,
    PdfProcessingParameters Parameters,
    TimeSpan Timeout,
    long MaxJsonBytes,
    long MaxOutputBytes);

public sealed record PdfEngineResult(
    bool Succeeded,
    string OutputPath,
    string EngineVersion,
    string? ErrorMessage,
    TimeSpan Duration,
    long OutputBytes = 0);

public sealed record PdfBookmarkInspection(
    string ObjectPath,
    string Title,
    int Level,
    int? PageNumber,
    string? NamedDestination,
    bool IsValid);

public sealed record PdfLinkInspection(
    string ObjectPath,
    int SourcePageNumber,
    PdfLinkTargetKind Kind,
    string Target,
    int? TargetPageNumber,
    string? NamedDestination,
    string? FileSpecification,
    bool IsValid);

public sealed record PdfNamedDestinationInspection(string Name, int? PageNumber, bool IsValid);

public sealed record PdfNavigationInspection(
    string EngineVersion,
    int? PageCount,
    string? PageMode,
    IReadOnlyList<PdfBookmarkInspection> Bookmarks,
    IReadOnlyList<PdfLinkInspection> Links,
    IReadOnlyList<PdfNamedDestinationInspection> NamedDestinations,
    bool IsEncrypted,
    bool HasSignature,
    bool SignatureValidityKnown,
    bool NavigationComplete,
    bool DestinationsComplete,
    IReadOnlyList<string> IncompleteReasons);

public interface IPdfProcessingEngine
{
    string EngineVersion { get; }
    Task<PdfNavigationInspection> InspectAsync(string sourcePath, long maxJsonBytes, CancellationToken cancellationToken = default);
    Task<PdfEngineResult> ExecuteAsync(PdfEngineRequest request, CancellationToken cancellationToken = default);
}
