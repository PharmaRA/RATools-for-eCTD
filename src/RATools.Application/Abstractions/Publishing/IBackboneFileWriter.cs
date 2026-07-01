using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Abstractions.Publishing;

public interface IBackboneFileWriter
{
    Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        string applicationNumber,
        string sequenceNumber,
        Guid publishJobId,
        string outputDirectoryPath,
        IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
        string reportFileName,
        string packageFileName,
        IReadOnlyCollection<EctdPublishedFile> publishedFiles,
        CancellationToken cancellationToken = default);
}
