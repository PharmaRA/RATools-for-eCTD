using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Infrastructure.Publishing;

public sealed partial class LocalBackboneFileWriter(
    IOptions<BackboneOutputOptions> options,
    ILogger<LocalBackboneFileWriter> logger) : IBackboneFileWriter
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "Pruned stale publish job run '{StaleJobDirectory}' (retention: {RetainJobRuns}).")]
    private static partial void LogJobRunPruned(ILogger logger, string staleJobDirectory, int retainJobRuns);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "Failed to prune stale publish job run '{StaleJobDirectory}'; publish output retention continues.")]
    private static partial void LogJobRunPruneFailed(ILogger logger, Exception exception, string staleJobDirectory);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning,
        Message = "Skipped pruning '{StaleJobDirectory}' because it is a reparse point.")]
    private static partial void LogJobRunPruneSkippedReparsePoint(ILogger logger, string staleJobDirectory);

    public async Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
        string applicationNumber,
        string sequenceNumber,
        Guid publishJobId,
        string outputDirectoryPath,
        IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
        string reportFileName,
        string packageFileName,
        IReadOnlyCollection<EctdPublishedFile> publishedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);
        ArgumentNullException.ThrowIfNull(generatedFiles);
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
        var md5Content = BuildIndexMd5(deliveryRoot);
        await File.WriteAllTextAsync(indexMd5Path, md5Content, cancellationToken);

        var reportPath = Path.Combine(reportDirectory, reportFileName);

        var packagePath = Path.Combine(packageDirectory, packageFileName);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(deliveryRoot, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);

        PruneOldJobRuns(applicationRoot, jobIdSegment);

        var fullPath = ResolveDeliveryPath(deliveryRoot, "index.xml");
        return (fullPath, reportPath, packagePath);
    }

    /// <summary>
    /// 保留策略：每次发布在 _jobs/{jobId} 下产生一份完整交付副本，从不清理会线性
    /// 吃满磁盘。发布成功后按 LastWriteTimeUtc 保留最近 N 份，只动 _jobs（工作副本），
    /// _artifacts 与 _packages 是交付物不清理。清理失败绝不让已成功的发布变失败。
    /// </summary>
    private void PruneOldJobRuns(string applicationRoot, string currentJobIdSegment)
    {
        var retainJobRuns = options.Value.RetainJobRuns;
        if (retainJobRuns <= 0)
        {
            return;
        }

        var jobsRoot = Path.GetFullPath(Path.Combine(applicationRoot, "_jobs"));
        if (!Directory.Exists(jobsRoot))
        {
            return;
        }

        var allowedPrefix = jobsRoot + Path.DirectorySeparatorChar;
        var staleDirectories = new DirectoryInfo(jobsRoot)
            .GetDirectories()
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Skip(retainJobRuns)
            .Where(directory => !string.Equals(directory.Name, currentJobIdSegment, StringComparison.OrdinalIgnoreCase));

        foreach (var staleDirectory in staleDirectories)
        {
            try
            {
                // 结构性防线：只删除 _jobs 的直接子目录；拒绝 reparse point，
                // 防止 junction/symlink 把递归删除引到 _jobs 之外。
                var fullPath = Path.GetFullPath(staleDirectory.FullName);
                if (!fullPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if ((staleDirectory.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    LogJobRunPruneSkippedReparsePoint(logger, fullPath);
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
                LogJobRunPruned(logger, fullPath, retainJobRuns);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogJobRunPruneFailed(logger, exception, staleDirectory.FullName);
            }
        }
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

    // FDA 惯例：index-md5.txt 仅包含 index.xml 自身的 MD5（格式为 "<md5>  index.xml"）。
    private static string BuildIndexMd5(string deliveryRoot)
    {
        var indexPath = Path.Combine(deliveryRoot, "index.xml");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException(
                $"index.xml was not found at '{indexPath}'; cannot build index-md5.txt.",
                indexPath);
        }

        return $"{ComputeMd5(indexPath)}  index.xml{Environment.NewLine}";
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
