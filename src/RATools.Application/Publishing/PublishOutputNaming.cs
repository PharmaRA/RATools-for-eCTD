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

    public static string BuildPublishReportFileName(string sequenceNumber, Guid jobId, int attemptCount = 0)
        => attemptCount > 0 ? $"publish-report-{sequenceNumber}-{jobId:N}-attempt-{attemptCount}.json"
            : $"publish-report-{sequenceNumber}-{jobId:N}.json";

    public static string BuildPublishReportPath(string outputPath, string sequenceNumber, Guid jobId, int attemptCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var jobIdSegment = jobId.ToString("N");
        var deliveryRoot = Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new InvalidOperationException($"Output path '{outputPath}' does not have a parent directory.");
        var deliveryRootInfo = new DirectoryInfo(deliveryRoot);
        var jobDirectory = deliveryRootInfo.Parent;
        var jobsDirectory = jobDirectory?.Parent;
        var newLayoutApplicationRoot = jobsDirectory?.Parent;

        if (string.Equals(jobDirectory?.Name, jobIdSegment, StringComparison.OrdinalIgnoreCase)
            && string.Equals(jobsDirectory?.Name, "_jobs", StringComparison.OrdinalIgnoreCase)
            && newLayoutApplicationRoot is not null)
        {
            return Path.Combine(newLayoutApplicationRoot.FullName, "_artifacts", sequenceNumber, jobIdSegment, BuildPublishReportFileName(sequenceNumber, jobId, attemptCount));
        }

        var legacyApplicationRoot = Path.GetDirectoryName(deliveryRoot)
            ?? throw new InvalidOperationException($"Delivery root '{deliveryRoot}' does not have a parent directory.");
        return Path.Combine(legacyApplicationRoot, "_artifacts", sequenceNumber, BuildPublishReportFileName(sequenceNumber, jobId, attemptCount));
    }
}
