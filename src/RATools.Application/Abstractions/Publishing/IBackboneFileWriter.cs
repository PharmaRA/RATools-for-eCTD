using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Publishing;

public interface IBackboneFileWriter
{
    Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        Guid applicationId,
        string sequenceNumber,
        string fileName,
        string content,
        string reportFileName,
        string packageFileName,
        string reportContent,
        IReadOnlyCollection<SubmissionDocument> documents,
        CancellationToken cancellationToken = default);
}
