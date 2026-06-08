using System.IO.Compression;
using System.Xml.Linq;
using System.Xml.XPath;
using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Publishing;

public sealed class PublishOutputVerifier
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
            var resolvedOutputDirectory = outputDirectory!;

            if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
            {
                var documentReferences = ReadDocumentReferences(outputPath!);
                foreach (var relativePath in documentReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var absolutePath = Path.Combine(resolvedOutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absolutePath))
                    {
                        findings.Add(new PublishIntegrityFindingDto(
                            "Error",
                            "MissingReferencedFile",
                            relativePath,
                            "A file referenced by the backbone XML was not found."));
                        missingFilesCount++;
                    }
                }
            }

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

                artifacts.Add(new PublishArtifactEvidenceDto(
                    "OutputFile",
                    outputFile.RelativePath,
                    outputFile.Path,
                    true,
                    new FileInfo(outputFile.Path).Length,
                    zipEntryPresent,
                    "OutputDirectory"));
            }
        }

        var isConsistent = missingFilesCount == 0 && missingZipEntriesCount == 0 && mismatchedArtifactsCount == 0;
        var summary = new PublishIntegritySummaryDto(isConsistent, missingFilesCount, missingZipEntriesCount, mismatchedArtifactsCount);
        var evidence = new PublishIntegrityEvidenceDto(artifacts, findings);
        return Task.FromResult(new PublishIntegrityVerificationDto(summary, evidence));
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

    private static IReadOnlyCollection<string> ReadDocumentReferences(string outputPath)
    {
        var document = XDocument.Load(outputPath);
        XNamespace[] xlinkNamespaces =
        [
            XNamespace.Get("http://www.w3.org/1999/xlink"),
            XNamespace.Get("http://www.w3c.org/1999/xlink")
        ];

        return document
            .Descendants()
            .Attributes()
            .Where(attribute => xlinkNamespaces.Contains(attribute.Name.Namespace) && attribute.Name.LocalName == "href")
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
