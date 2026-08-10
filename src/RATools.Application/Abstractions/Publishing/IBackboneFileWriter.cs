using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Abstractions.Publishing;

public interface IBackboneFileWriter
{
    Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        Guid applicationId,
        string sequenceNumber,
        Guid publishJobId,
        IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
        string reportFileName,
        string packageFileName,
        IReadOnlyCollection<EctdPublishedFile> publishedFiles,
        CancellationToken cancellationToken = default);
}
