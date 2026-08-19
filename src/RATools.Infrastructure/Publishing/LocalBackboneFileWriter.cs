using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Domain.Common;

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
        Guid applicationId,
        string sequenceNumber,
        Guid publishJobId,
        IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
        string reportFileName,
        string packageFileName,
        IReadOnlyCollection<EctdPublishedFile> publishedFiles,
        IReadOnlyCollection<StandardsAsset>? standardsAssets = null,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("Application id must not be empty.", nameof(applicationId));
        }

        ArgumentNullException.ThrowIfNull(generatedFiles);
        ArgumentNullException.ThrowIfNull(publishedFiles);

        var safeSequenceNumber = PortablePathSegment.NormalizeAndValidate(sequenceNumber, nameof(sequenceNumber));
        var safeReportFileName = PortablePathSegment.NormalizeAndValidate(reportFileName, nameof(reportFileName));
        var safePackageFileName = PortablePathSegment.NormalizeAndValidate(packageFileName, nameof(packageFileName));

        var rootPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("Backbone output root path is not configured.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        var jobIdSegment = publishJobId.ToString("N");
        // Resolve every data-derived path before creating directories so a rejected path cannot mutate disk.
        var applicationRoot = ResolveDescendantPath(fullRootPath, applicationId.ToString("N"), "Application output root");
        var jobsRoot = ResolveDescendantPath(applicationRoot, "_jobs", "Publish jobs root");
        var jobRoot = ResolveDescendantPath(jobsRoot, jobIdSegment, "Publish job root");
        var deliveryRoot = ResolveDescendantPath(jobRoot, safeSequenceNumber, "Publish delivery root");
        var artifactsRoot = ResolveDescendantPath(applicationRoot, "_artifacts", "Publish artifacts root");
        var sequenceArtifactsRoot = ResolveDescendantPath(artifactsRoot, safeSequenceNumber, "Sequence artifacts root");
        var reportDirectory = ResolveDescendantPath(sequenceArtifactsRoot, jobIdSegment, "Publish report directory");
        var packagesRoot = ResolveDescendantPath(applicationRoot, "_packages", "Publish packages root");
        var packageDirectory = ResolveDescendantPath(packagesRoot, safeSequenceNumber, "Sequence package directory");
        var generatedDestinations = generatedFiles
            .Select(file => (File: file, DestinationPath: ResolveDeliveryPath(deliveryRoot, file.RelativePath)))
            .ToArray();
        var publishedDestinations = publishedFiles
            .Select(file => (File: file, DestinationPath: ResolveDeliveryPath(deliveryRoot, file.Href)))
            .ToArray();
        var indexMd5Path = ResolveDescendantPath(deliveryRoot, "index-md5.txt", "Index MD5 path");
        var reportPath = ResolveDescendantPath(reportDirectory, safeReportFileName, "Publish report path");
        var packagePath = ResolveDescendantPath(packageDirectory, safePackageFileName, "Publish package path");
        var fullPath = ResolveDescendantPath(deliveryRoot, "index.xml", "Backbone index path");
        var standardsAssetCopies = BuildStandardsAssetCopyPlan(deliveryRoot, standardsAssets);

        var plannedPaths = generatedDestinations.Select(item => item.DestinationPath)
            .Concat(publishedDestinations.Select(item => item.DestinationPath))
            .Concat(standardsAssetCopies.Select(item => item.DestinationPath))
            .Append(indexMd5Path)
            .Append(reportPath)
            .Append(packagePath)
            .Append(fullPath);
        foreach (var plannedPath in plannedPaths)
        {
            EnsureNoReparsePoints(fullRootPath, plannedPath);
        }

        Directory.CreateDirectory(deliveryRoot);
        Directory.CreateDirectory(reportDirectory);
        Directory.CreateDirectory(packageDirectory);

        foreach (var (generatedFile, destinationPath) in generatedDestinations)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, generatedFile.Content, cancellationToken);
        }

        foreach (var (publishedFile, destinationPath) in publishedDestinations)
        {
            if (!File.Exists(publishedFile.SourcePath))
            {
                throw new FileNotFoundException(
                    $"Published source file '{publishedFile.SourcePath}' was not found.",
                    publishedFile.SourcePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var sourceStream = File.OpenRead(publishedFile.SourcePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        CopyStandardsAssets(standardsAssetCopies);

        var md5Content = BuildIndexMd5(deliveryRoot);
        await File.WriteAllTextAsync(indexMd5Path, md5Content, cancellationToken);

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(deliveryRoot, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);

        PruneOldJobRuns(jobsRoot, jobIdSegment);

        return (fullPath, reportPath, packagePath);
    }

    /// <summary>
    /// 保留策略：每次发布在 _jobs/{jobId} 下产生一份完整交付副本，从不清理会线性
    /// 吃满磁盘。发布成功后按 LastWriteTimeUtc 保留最近 N 份，只动 _jobs（工作副本），
    /// _artifacts 与 _packages 是交付物不清理。清理失败绝不让已成功的发布变失败。
    /// </summary>
    private void PruneOldJobRuns(string jobsRoot, string currentJobIdSegment)
    {
        var retainJobRuns = options.Value.RetainJobRuns;
        if (retainJobRuns <= 0)
        {
            return;
        }

        if (!Directory.Exists(jobsRoot))
        {
            return;
        }

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
                if (!IsDescendantPath(jobsRoot, fullPath))
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
        return ResolveDescendantPath(deliveryRoot, relativePath, "Package relative path");
    }

    private static string ResolveDescendantPath(string rootPath, string relativePath, string pathDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (IsPortableRootedPath(relativePath))
        {
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' must not be rooted.");
        }

        if (relativePath.Contains('\\'))
        {
            throw new InvalidOperationException(
                $"{pathDescription} '{relativePath}' must use canonical forward-slash separators.");
        }

        var pathSegments = relativePath.Split('/');
        if (pathSegments.Any(string.IsNullOrEmpty))
        {
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' contains an empty path segment.");
        }

        string[] safePathSegments;
        try
        {
            safePathSegments = pathSegments
                .Select(segment => PortablePathSegment.NormalizeAndValidate(segment, nameof(relativePath)))
                .ToArray();
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{pathDescription} '{relativePath}' contains a non-portable path segment.",
                exception);
        }

        if (!safePathSegments.SequenceEqual(pathSegments, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' contains surrounding whitespace.");
        }

        var normalizedRelativePath = Path.Combine(safePathSegments);
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(fullRootPath, normalizedRelativePath));
        if (!IsDescendantPath(fullRootPath, fullPath))
        {
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' escapes '{fullRootPath}'.");
        }

        return fullPath;
    }

    private static bool IsPortableRootedPath(string path)
        => Path.IsPathRooted(path)
           || path.StartsWith('/')
           || path.StartsWith('\\')
           || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');

    private static bool IsDescendantPath(string rootPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(candidatePath));
        return relativePath != ".."
               && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !Path.IsPathRooted(relativePath)
               && relativePath != ".";
    }

    private static void EnsureNoReparsePoints(string rootPath, string candidatePath)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullCandidatePath = Path.GetFullPath(candidatePath);
        if (!IsDescendantPath(fullRootPath, fullCandidatePath))
        {
            throw new InvalidOperationException($"Path '{fullCandidatePath}' escapes publish root '{fullRootPath}'.");
        }

        EnsurePathComponentIsNotReparsePoint(fullRootPath);
        var currentPath = fullRootPath;
        foreach (var segment in Path.GetRelativePath(fullRootPath, fullCandidatePath)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!TryGetAttributes(currentPath, out var attributes))
            {
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                throw new InvalidOperationException(
                    $"Publish path '{currentPath}' is a reparse point and cannot be used as an output destination.");
            }
        }
    }

    private static void EnsurePathComponentIsNotReparsePoint(string path)
    {
        if (TryGetAttributes(path, out var attributes)
            && (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            throw new InvalidOperationException(
                $"Publish root '{path}' is a reparse point and cannot be used as an output destination.");
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static (string SourcePath, string DestinationPath)[] BuildStandardsAssetCopyPlan(
        string deliveryRoot,
        IReadOnlyCollection<StandardsAsset>? assets)
    {
        if (assets is not null)
        {
            return assets
                .Select(asset => BuildStandardsAssetCopyPlan(deliveryRoot, asset))
                .GroupBy(item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException($"Multiple standards assets target '{group.Key}'."))
                .ToArray();
        }

        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "reference", "dtd");
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Standards DTD directory was not found at '{sourceDirectory}'.");
        }

        var destinationDirectory = ResolveDescendantPath(deliveryRoot, "util/dtd", "Standards asset directory");
        return Directory.GetFiles(sourceDirectory, "*.dtd", SearchOption.TopDirectoryOnly)
            .Select(sourcePath => (
                SourcePath: sourcePath,
                DestinationPath: ResolveDescendantPath(
                    destinationDirectory,
                    Path.GetFileName(sourcePath),
                    "Standards asset path")))
            .ToArray();
    }

    private static (string SourcePath, string DestinationPath) BuildStandardsAssetCopyPlan(
        string deliveryRoot,
        StandardsAsset asset)
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            asset.LocalRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Standards asset '{asset.Key}' was not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        var packageDirectory = string.Equals(asset.Category, "XSL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xsl", StringComparison.OrdinalIgnoreCase)
            ? "util/style"
            : "util/dtd";
        var destinationDirectory = ResolveDescendantPath(deliveryRoot, packageDirectory, "Standards asset directory");
        return (
            sourcePath,
            ResolveDescendantPath(destinationDirectory, Path.GetFileName(sourcePath), "Standards asset path"));
    }

    private static void CopyStandardsAssets(
        IReadOnlyCollection<(string SourcePath, string DestinationPath)> standardsAssets)
    {
        foreach (var (sourcePath, destinationPath) in standardsAssets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    // FDA 惯例：index-md5.txt 仅包含 index.xml 自身的 MD5（格式为 "<md5>  index.xml"）。
    private static string BuildIndexMd5(string deliveryRoot)
    {
        var indexPath = ResolveDescendantPath(deliveryRoot, "index.xml", "Backbone index path");
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
