using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Publishing;

public sealed class LocalBackboneFileWriter(IOptions<BackboneOutputOptions> options) : IBackboneFileWriter
{
    public async Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        string applicationNumber,
        string sequenceNumber,
        Guid publishJobId,
        string outputDirectoryPath,
        string fileName,
        string content,
        string reportFileName,
        string packageFileName,
        string reportContent,
        IReadOnlyCollection<SubmissionDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(reportContent);
        ArgumentNullException.ThrowIfNull(documents);

        var rootPath = string.IsNullOrWhiteSpace(outputDirectoryPath)
            ? options.Value.RootPath
            : outputDirectoryPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("Backbone output root path is not configured.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        var jobIdSegment = publishJobId.ToString("N");
        var applicationRoot = Path.Combine(fullRootPath, applicationNumber);
        var deliveryRoot = Path.Combine(applicationRoot, "_jobs", jobIdSegment, sequenceNumber);
        var reportDirectory = Path.Combine(applicationRoot, "_artifacts", sequenceNumber, jobIdSegment);
        var packageDirectory = Path.Combine(applicationRoot, "_packages", sequenceNumber);
        Directory.CreateDirectory(deliveryRoot);
        Directory.CreateDirectory(reportDirectory);
        Directory.CreateDirectory(packageDirectory);

        foreach (var document in documents)
        {
            if (!File.Exists(document.StoragePath))
            {
                continue;
            }

            var relativePath = PublishOutputNaming.BuildPublishedDocumentRelativePath(document, sequenceNumber);
            var destinationPath = Path.Combine(deliveryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var sourceStream = File.OpenRead(document.StoragePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        var fullPath = Path.Combine(deliveryRoot, fileName);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);

        var indexMd5Path = Path.Combine(deliveryRoot, "index-md5.txt");
        var md5Content = BuildMd5Manifest(deliveryRoot, indexMd5Path);
        await File.WriteAllTextAsync(indexMd5Path, md5Content, cancellationToken);

        var reportPath = Path.Combine(reportDirectory, reportFileName);
        await File.WriteAllTextAsync(reportPath, reportContent, cancellationToken);

        var packagePath = Path.Combine(packageDirectory, packageFileName);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(deliveryRoot, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);

        return (fullPath, reportPath, packagePath);
    }

    private static string BuildMd5Manifest(string deliveryRoot, string indexMd5Path)
    {
        var builder = new StringBuilder();
        var files = Directory.GetFiles(deliveryRoot, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(indexMd5Path), StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var file in files)
        {
            builder.Append(ComputeMd5(file));
            builder.Append("  ");
            builder.Append(Path.GetRelativePath(deliveryRoot, file).Replace('\\', '/'));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
