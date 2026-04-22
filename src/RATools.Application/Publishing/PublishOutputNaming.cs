using RATools.Domain.Documents;

namespace RATools.Application.Publishing;

public static class PublishOutputNaming
{
    public static string BuildPublishedDocumentFileName(SubmissionDocument document)
    {
        return Path.GetFileName(document.StoragePath);
    }

    public static string BuildPublishedDocumentRelativePath(SubmissionDocument document, string sequenceNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var normalizedPath = Path.GetFullPath(document.StoragePath);
        var segments = normalizedPath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var sequenceIndex = Array.FindLastIndex(segments, segment => string.Equals(segment, sequenceNumber, StringComparison.OrdinalIgnoreCase));
        if (sequenceIndex >= 0 && sequenceIndex < segments.Length - 1)
        {
            return string.Join('/', segments[(sequenceIndex + 1)..]);
        }

        return BuildPublishedDocumentFileName(document);
    }

    public static string BuildPublishReportPath(string outputPath, string sequenceNumber, Guid jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var deliveryRoot = Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new InvalidOperationException($"Output path '{outputPath}' does not have a parent directory.");
        var applicationRoot = Path.GetDirectoryName(deliveryRoot)
            ?? throw new InvalidOperationException($"Delivery root '{deliveryRoot}' does not have a parent directory.");
        return Path.Combine(applicationRoot, "_artifacts", sequenceNumber, $"publish-report-{sequenceNumber}-{jobId:N}.json");
    }
}
