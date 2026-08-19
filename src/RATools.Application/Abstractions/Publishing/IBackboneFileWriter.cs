using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;

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
        IReadOnlyCollection<StandardsAsset>? standardsAssets = null,
        CancellationToken cancellationToken = default);
}
