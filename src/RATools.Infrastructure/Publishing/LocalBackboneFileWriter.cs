using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Infrastructure.Publishing;

public sealed class LocalBackboneFileWriter(IOptions<BackboneOutputOptions> options) : IBackboneFileWriter
{
    public async Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        string applicationNumber,
        string sequenceNumber,
        Guid publishJobId,
        string outputDirectoryPath,
        IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
        string reportFileName,
        string packageFileName,
        string reportContent,
        IReadOnlyCollection<EctdPublishedFile> publishedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);
        ArgumentNullException.ThrowIfNull(generatedFiles);
        ArgumentNullException.ThrowIfNull(reportContent);
        ArgumentNullException.ThrowIfNull(publishedFiles);

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

        foreach (var generatedFile in generatedFiles)
        {
            var destinationPath = ResolveDeliveryPath(deliveryRoot, generatedFile.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, generatedFile.Content, cancellationToken);
        }

        foreach (var publishedFile in publishedFiles)
        {
            if (!File.Exists(publishedFile.SourcePath))
            {
                throw new FileNotFoundException(
                    $"Published source file '{publishedFile.SourcePath}' was not found.",
                    publishedFile.SourcePath);
            }

            var destinationPath = ResolveDeliveryPath(deliveryRoot, publishedFile.Href);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var sourceStream = File.OpenRead(publishedFile.SourcePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        CopyStandardsAssets(deliveryRoot);

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

        var fullPath = ResolveDeliveryPath(deliveryRoot, "index.xml");
        return (fullPath, reportPath, packagePath);
    }

    private static string ResolveDeliveryPath(string deliveryRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Package relative path '{relativePath}' must not be rooted.");
        }

        var normalizedRelativePath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var fullDeliveryRoot = Path.GetFullPath(deliveryRoot);
        var fullPath = Path.GetFullPath(Path.Combine(fullDeliveryRoot, normalizedRelativePath));
        var allowedPrefix = fullDeliveryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullDeliveryRoot
            : fullDeliveryRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Package relative path '{relativePath}' escapes the delivery root.");
        }

        return fullPath;
    }

    private static void CopyStandardsAssets(string deliveryRoot)
    {
        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "reference", "dtd");
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Standards DTD directory was not found at '{sourceDirectory}'.");
        }

        var destinationDirectory = Path.Combine(deliveryRoot, "util", "dtd");
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourcePath in Directory.GetFiles(sourceDirectory, "*.dtd", SearchOption.TopDirectoryOnly))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
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
