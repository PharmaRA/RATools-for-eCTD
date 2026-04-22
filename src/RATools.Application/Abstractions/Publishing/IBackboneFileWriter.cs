using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Publishing;

public interface IBackboneFileWriter
{
    Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        string applicationNumber,
        string sequenceNumber,
        string outputDirectoryPath,
        string fileName,
        string content,
        string reportFileName,
        string packageFileName,
        string reportContent,
        IReadOnlyCollection<SubmissionDocument> documents,
        CancellationToken cancellationToken = default);
}
