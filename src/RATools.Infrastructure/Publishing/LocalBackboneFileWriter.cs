using System.IO.Compression;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Publishing;

public sealed class LocalBackboneFileWriter(IOptions<BackboneOutputOptions> options) : IBackboneFileWriter
{
    public async Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        Guid applicationId,
        string sequenceNumber,
        string fileName,
        string content,
        string reportFileName,
        string reportContent,
        IReadOnlyCollection<SubmissionDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportFileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(reportContent);
        ArgumentNullException.ThrowIfNull(documents);

        var rootPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("Backbone output root path is not configured.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        var outputDirectory = Path.Combine(fullRootPath, applicationId.ToString("N"), sequenceNumber);
        Directory.CreateDirectory(outputDirectory);

        var documentsDirectory = Path.Combine(outputDirectory, "documents");
        Directory.CreateDirectory(documentsDirectory);

        foreach (var document in documents)
        {
            if (!File.Exists(document.StoragePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(documentsDirectory, document.FileName);
            await using var sourceStream = File.OpenRead(document.StoragePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        var fullPath = Path.Combine(outputDirectory, fileName);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);

        var reportPath = Path.Combine(outputDirectory, reportFileName);
        await File.WriteAllTextAsync(reportPath, reportContent, cancellationToken);

        var packagePath = Path.Combine(fullRootPath, applicationId.ToString("N"), $"{sequenceNumber}.zip");
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(outputDirectory, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);

        return (fullPath, reportPath, packagePath);
    }
}
