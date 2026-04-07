using RATools.Domain.Documents;

namespace RATools.Application.Publishing;

public static class PublishOutputNaming
{
    public static string BuildPublishedDocumentFileName(SubmissionDocument document)
    {
        return $"{document.Id:N}_{document.FileName}";
    }

    public static string BuildPublishedDocumentRelativePath(SubmissionDocument document)
    {
        return $"documents/{BuildPublishedDocumentFileName(document)}";
    }
}
