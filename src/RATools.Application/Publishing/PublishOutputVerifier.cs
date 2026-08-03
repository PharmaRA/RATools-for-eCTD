using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Publishing;

/// <summary>
/// 发布输出完整性核验。除文件存在性与 zip 条目外，还覆盖四类官方验证器高频拒收项：
/// (1) checksum 一致性——backbone 声明的 md5 与实际文件重算值必须一致；
/// (2) 孤儿文件反向扫描——磁盘上存在但未被任何 backbone 引用的文件；
/// (3) DTD 交付完整性——DOCTYPE SystemId 引用的 DTD 必须真实存在于包内；
/// (4) 空文件夹——整棵子树不含任何文件的目录被判为提交结构问题。
/// backbone 的 href 相对于**声明它的 XML 文件所在目录**解析（index.xml 在包根，
/// 区域 backbone 在 m1/&lt;region&gt;/ 下），跨序列引用（../ 逃出包根）不在本包核验范围。
/// </summary>
public sealed partial class PublishOutputVerifier
{
    public Task<PublishIntegrityVerificationDto> VerifyAsync(
        string? outputPath,
        string? reportPath,
        string? packagePath,
        CancellationToken cancellationToken = default)
    {
        var missingFilesCount = 0;
        var mismatchedArtifactsCount = 0;
        var missingZipEntriesCount = 0;
        var artifacts = new List<PublishArtifactEvidenceDto>();
        var findings = new List<PublishIntegrityFindingDto>();

        AddTopLevelArtifact("BackboneXml", outputPath, artifacts, findings, ref missingFilesCount);
        AddTopLevelArtifact("PublishReport", reportPath, artifacts, findings, ref missingFilesCount);
        AddTopLevelArtifact("PackageZip", packagePath, artifacts, findings, ref missingFilesCount);

        var outputDirectory = string.IsNullOrWhiteSpace(outputPath)
            ? null
            : Path.GetDirectoryName(outputPath);
        var outputDirectoryExists = !string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory);

        if (!outputDirectoryExists)
        {
            findings.Add(new PublishIntegrityFindingDto(
                "Error",
                "OutputDirectoryMissing",
                outputDirectory,
                "The publish output directory is missing or could not be resolved."));
            mismatchedArtifactsCount++;
        }
        else
        {
            var resolvedOutputDirectory = Path.GetFullPath(outputDirectory!);
            var backboneFiles = CollectBackboneFiles(resolvedOutputDirectory, outputPath);
            var referencedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var backboneFile in backboneFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyBackboneReferences(
                    backboneFile,
                    resolvedOutputDirectory,
                    referencedRelativePaths,
                    findings,
                    ref missingFilesCount,
                    ref mismatchedArtifactsCount,
                    cancellationToken);
            }

            VerifyIndexMd5(resolvedOutputDirectory, outputPath, findings, ref mismatchedArtifactsCount);

            var outputFiles = Directory.GetFiles(resolvedOutputDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Select(file => new
                {
                    Path = file,
                    RelativePath = Path.GetRelativePath(resolvedOutputDirectory, file).Replace('\\', '/')
                })
                .ToArray();

            HashSet<string>? zipEntries = null;
            if (!string.IsNullOrWhiteSpace(packagePath) && File.Exists(packagePath))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(packagePath);
                    zipEntries = archive.Entries
                        .Select(x => x.FullName.Replace('\\', '/'))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (InvalidDataException)
                {
                    findings.Add(new PublishIntegrityFindingDto(
                        "Error",
                        "InvalidZip",
                        packagePath,
                        "The publish package could not be opened as a zip archive."));
                    mismatchedArtifactsCount++;
                }
            }

            var backboneRelativePath = !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath)
                ? Path.GetRelativePath(resolvedOutputDirectory, outputPath).Replace('\\', '/')
                : null;
            if (zipEntries is not null && backboneRelativePath is not null)
            {
                var backboneZipEntryPresent = zipEntries.Contains(backboneRelativePath);
                var backboneIndex = artifacts.FindIndex(x => x.Role == "BackboneXml");
                if (backboneIndex >= 0)
                {
                    artifacts[backboneIndex] = artifacts[backboneIndex] with
                    {
                        RelativePath = backboneRelativePath,
                        ZipEntryPresent = backboneZipEntryPresent
                    };
                }

                if (!backboneZipEntryPresent)
                {
                    findings.Add(new PublishIntegrityFindingDto(
                        "Error",
                        "MissingZipEntry",
                        backboneRelativePath,
                        "An output file is missing from the publish package zip."));
                    missingZipEntriesCount++;
                }
            }

            // 孤儿扫描白名单：backbone 自身、区域 backbone、index-md5.txt 清单与 util/ 下的标准资产。
            var orphanWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index-md5.txt" };
            if (backboneRelativePath is not null)
            {
                orphanWhitelist.Add(backboneRelativePath);
            }

            foreach (var backboneFile in backboneFiles)
            {
                orphanWhitelist.Add(backboneFile.RelativePath);
            }

            foreach (var outputFile in outputFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsSamePath(outputFile.Path, outputPath))
                {
                    continue;
                }

                bool? zipEntryPresent = null;
                if (zipEntries is not null && !IsSamePath(outputFile.Path, packagePath))
                {
                    zipEntryPresent = zipEntries.Contains(outputFile.RelativePath);

                    if (!zipEntryPresent.Value)
                    {
                        findings.Add(new PublishIntegrityFindingDto(
                            "Error",
                            "MissingZipEntry",
                            outputFile.RelativePath,
                            "An output file is missing from the publish package zip."));
                        missingZipEntriesCount++;
                    }
                }

                var isWhitelisted = orphanWhitelist.Contains(outputFile.RelativePath)
                    || outputFile.RelativePath.StartsWith("util/", StringComparison.OrdinalIgnoreCase);
                if (!isWhitelisted && !referencedRelativePaths.Contains(outputFile.RelativePath))
                {
                    // 官方验证器将未被 backbone 引用的交付文件判为问题内容；
                    // 此处报 Warning 供审阅，不影响 isConsistent 判定。
                    findings.Add(new PublishIntegrityFindingDto(
                        "Warning",
                        "OrphanFile",
                        outputFile.RelativePath,
                        "The file exists in the delivery output but is not referenced by any backbone."));
                }

                artifacts.Add(new PublishArtifactEvidenceDto(
                    "OutputFile",
                    outputFile.RelativePath,
                    outputFile.Path,
                    true,
                    new FileInfo(outputFile.Path).Length,
                    zipEntryPresent,
                    "OutputDirectory"));
            }

            VerifyNoEmptyDirectories(resolvedOutputDirectory, findings, cancellationToken);
        }

        var isConsistent = missingFilesCount == 0 && missingZipEntriesCount == 0 && mismatchedArtifactsCount == 0;
        var summary = new PublishIntegritySummaryDto(isConsistent, missingFilesCount, missingZipEntriesCount, mismatchedArtifactsCount);
        var evidence = new PublishIntegrityEvidenceDto(artifacts, findings);
        return Task.FromResult(new PublishIntegrityVerificationDto(summary, evidence));
    }

    private sealed record BackboneFileInfo(string Path, string RelativePath, string Directory);

    private sealed record BackboneLeafReference(string Href, string? Checksum, string? ChecksumType);

    private static IReadOnlyList<BackboneFileInfo> CollectBackboneFiles(string outputRoot, string? outputPath)
    {
        var files = new List<BackboneFileInfo>();
        if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            files.Add(new BackboneFileInfo(
                fullPath,
                Path.GetRelativePath(outputRoot, fullPath).Replace('\\', '/'),
                Path.GetDirectoryName(fullPath)!));
        }

        // 区域 backbone（us-regional.xml / eu-regional.xml）的 leaf 只在区域文件中声明，
        // 不读它们会把全部 m1 文件误判为孤儿。
        foreach (var regionalPath in Directory.EnumerateFiles(outputRoot, "*-regional.xml", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(regionalPath);
            files.Add(new BackboneFileInfo(
                fullPath,
                Path.GetRelativePath(outputRoot, fullPath).Replace('\\', '/'),
                Path.GetDirectoryName(fullPath)!));
        }

        return files;
    }

    private void VerifyBackboneReferences(
        BackboneFileInfo backboneFile,
        string outputRoot,
        HashSet<string> referencedRelativePaths,
        List<PublishIntegrityFindingDto> findings,
        ref int missingFilesCount,
        ref int mismatchedArtifactsCount,
        CancellationToken cancellationToken)
    {
        var allowedPrefix = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        var dtdSystemId = ReadDoctypeSystemId(backboneFile.Path);
        if (!string.IsNullOrWhiteSpace(dtdSystemId))
        {
            var dtdAbsolutePath = Path.GetFullPath(Path.Combine(
                backboneFile.Directory,
                dtdSystemId.Replace('/', Path.DirectorySeparatorChar)));
            if (dtdAbsolutePath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var dtdRelativePath = Path.GetRelativePath(outputRoot, dtdAbsolutePath).Replace('\\', '/');
                referencedRelativePaths.Add(dtdRelativePath);
                if (!File.Exists(dtdAbsolutePath))
                {
                    findings.Add(new PublishIntegrityFindingDto(
                        "Error",
                        "MissingDtdAsset",
                        dtdRelativePath,
                        $"The DTD referenced by '{backboneFile.RelativePath}' is missing from the delivery package."));
                    missingFilesCount++;
                }
            }
        }

        foreach (var leafReference in ReadLeafReferences(backboneFile.Path))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsExternalReference(leafReference.Href))
            {
                continue;
            }

            var absolutePath = Path.GetFullPath(Path.Combine(
                backboneFile.Directory,
                leafReference.Href.Replace('/', Path.DirectorySeparatorChar)));
            if (!absolutePath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // 跨序列引用（如 modified-file 的 ../0000/…）不属于本交付包核验范围。
                continue;
            }

            var relativePath = Path.GetRelativePath(outputRoot, absolutePath).Replace('\\', '/');
            referencedRelativePaths.Add(relativePath);

            if (!File.Exists(absolutePath))
            {
                findings.Add(new PublishIntegrityFindingDto(
                    "Error",
                    "MissingReferencedFile",
                    relativePath,
                    "A file referenced by the backbone XML was not found."));
                missingFilesCount++;
                continue;
            }

            if (string.Equals(leafReference.ChecksumType, "md5", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(leafReference.Checksum))
            {
                var actualMd5 = ComputeMd5(absolutePath);
                if (!string.Equals(actualMd5, leafReference.Checksum.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new PublishIntegrityFindingDto(
                        "Error",
                        "ChecksumMismatch",
                        relativePath,
                        $"Backbone-declared MD5 '{leafReference.Checksum}' does not match the actual file MD5 '{actualMd5}'."));
                    mismatchedArtifactsCount++;
                }
            }
        }
    }

    private static void VerifyIndexMd5(
        string outputRoot,
        string? outputPath,
        List<PublishIntegrityFindingDto> findings,
        ref int mismatchedArtifactsCount)
    {
        var indexMd5Path = Path.Combine(outputRoot, "index-md5.txt");
        if (!File.Exists(indexMd5Path) || string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            return;
        }

        var firstLine = File.ReadLines(indexMd5Path).FirstOrDefault() ?? string.Empty;
        var declaredMd5 = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(declaredMd5))
        {
            return;
        }

        var actualMd5 = ComputeMd5(outputPath);
        if (!string.Equals(declaredMd5, actualMd5, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new PublishIntegrityFindingDto(
                "Error",
                "IndexMd5Mismatch",
                "index-md5.txt",
                $"index-md5.txt declares MD5 '{declaredMd5}' but index.xml computes to '{actualMd5}'."));
            mismatchedArtifactsCount++;
        }
    }

    // 官方验证器把交付包里的空文件夹判为提交结构问题。与 OrphanFile 同为 Warning 级：
    // 空目录可修可解释，不该阻断一次已成功的发布，故不翻转 isConsistent。
    private static void VerifyNoEmptyDirectories(
        string outputRoot,
        List<PublishIntegrityFindingDto> findings,
        CancellationToken cancellationToken)
    {
        foreach (var directory in EnumerateChildDirectories(outputRoot))
        {
            CollectEmptyDirectories(directory, outputRoot, findings, cancellationToken);
        }
    }

    /// <summary>
    /// 递归判定子树是否含任何文件，并就地收集空目录 finding。
    /// 嵌套目录整体为空时只报最外层——报一串嵌套空目录只是噪声。
    /// </summary>
    private static bool CollectEmptyDirectories(
        string directory,
        string outputRoot,
        List<PublishIntegrityFindingDto> findings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var subtreeHasFile = Directory.EnumerateFiles(directory).Any();
        var nestedFindings = new List<PublishIntegrityFindingDto>();
        foreach (var child in EnumerateChildDirectories(directory))
        {
            subtreeHasFile |= CollectEmptyDirectories(child, outputRoot, nestedFindings, cancellationToken);
        }

        if (!subtreeHasFile)
        {
            findings.Add(new PublishIntegrityFindingDto(
                "Warning",
                "EmptyFolder",
                Path.GetRelativePath(outputRoot, directory).Replace('\\', '/'),
                "The delivery output contains a folder without any file; official validators treat empty folders as a submission structure issue."));
            return false;
        }

        findings.AddRange(nestedFindings);
        return true;
    }

    // 枚举顺序按文件系统实现而定，排序保证 finding 顺序可预期。
    private static IEnumerable<string> EnumerateChildDirectories(string directory)
        => Directory.EnumerateDirectories(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

    private static bool IsExternalReference(string href)
        => string.IsNullOrWhiteSpace(href)
            || href.StartsWith('#')
            || href.Contains("://", StringComparison.Ordinal)
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    // 只做文本头部扫描取 SystemId：用 XmlReader 解析 DOCTYPE 需要放开 DtdProcessing，
    // 而这里核验的是外部产物，必须保持零外部实体解析。
    [GeneratedRegex("<!DOCTYPE[^>]*SYSTEM\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DoctypeSystemIdRegex();

    private static string? ReadDoctypeSystemId(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[4096];
        var read = reader.Read(buffer, 0, buffer.Length);
        var head = new string(buffer, 0, read);
        var match = DoctypeSystemIdRegex().Match(head);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsSamePath(string path, string? otherPath)
    {
        if (string.IsNullOrWhiteSpace(otherPath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(otherPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTopLevelArtifact(
        string role,
        string? path,
        ICollection<PublishArtifactEvidenceDto> artifacts,
        ICollection<PublishIntegrityFindingDto> findings,
        ref int missingFilesCount)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var sizeBytes = exists ? new FileInfo(path!).Length : 0;
        artifacts.Add(new PublishArtifactEvidenceDto(
            role,
            null,
            path,
            exists,
            sizeBytes,
            null,
            "TopLevelArtifact"));

        if (string.IsNullOrWhiteSpace(path))
        {
            findings.Add(new PublishIntegrityFindingDto(
                "Error",
                "EmptyArtifactPath",
                path,
                $"The {role} artifact path is empty."));
            missingFilesCount++;
            return;
        }

        if (!exists)
        {
            findings.Add(new PublishIntegrityFindingDto(
                "Error",
                "MissingTopLevelArtifact",
                path,
                $"The {role} artifact was not found."));
            missingFilesCount++;
        }
    }

    private static IReadOnlyCollection<BackboneLeafReference> ReadLeafReferences(string backbonePath)
    {
        // 安全解析：忽略 DTD、禁用外部实体解析（核验目标是不受信任的输出产物）。
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(backbonePath, settings);
        var document = XDocument.Load(reader);

        XNamespace[] xlinkNamespaces =
        [
            XNamespace.Get("http://www.w3.org/1999/xlink"),
            XNamespace.Get("http://www.w3c.org/1999/xlink")
        ];

        return document
            .Descendants()
            .Select(element => new
            {
                Element = element,
                Href = element.Attributes()
                    .FirstOrDefault(attribute => xlinkNamespaces.Contains(attribute.Name.Namespace) && attribute.Name.LocalName == "href")
                    ?.Value
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Href))
            .Select(x => new BackboneLeafReference(
                x.Href!,
                x.Element.Attribute("checksum")?.Value,
                x.Element.Attribute("checksum-type")?.Value))
            .ToArray();
    }
}
