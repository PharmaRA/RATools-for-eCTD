using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Publishing;

public interface IBackboneFileWriter
{
    Task<(string FilePath, string PackagePath)> SaveAsync(
        Guid applicationId,
        string sequenceNumber,
        string fileName,
        string content,
        IReadOnlyCollection<SubmissionDocument> documents,
        CancellationToken cancellationToken = default);
}
