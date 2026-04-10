using System.IO.Compression;
using System.Xml.Linq;
using System.Xml.XPath;
using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Publishing;

public sealed class PublishOutputVerifier
{
    public Task<PublishIntegritySummaryDto> VerifyAsync(
        string? outputPath,
        string? reportPath,
        string? packagePath,
        CancellationToken cancellationToken = default)
    {
        var missingFilesCount = 0;
        var mismatchedArtifactsCount = 0;
        var missingZipEntriesCount = 0;

        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            missingFilesCount++;
        }

        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            missingFilesCount++;
        }

        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            missingFilesCount++;
        }

        if (missingFilesCount == 0)
        {
            var outputDirectory = Path.GetDirectoryName(outputPath!);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                missingFilesCount++;
                mismatchedArtifactsCount++;
            }
            else
            {
                var documentReferences = ReadDocumentReferences(outputPath!);
                foreach (var relativePath in documentReferences)
                {
                    var absolutePath = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absolutePath))
                    {
                        missingFilesCount++;
                    }
                }

                var expectedEntries = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(outputDirectory, file).Replace('\\', '/'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                try
                {
                    using var archive = ZipFile.OpenRead(packagePath!);
                    var zipEntries = archive.Entries.Select(x => x.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    missingZipEntriesCount += expectedEntries.Count(entry => !zipEntries.Contains(entry));
                }
                catch (InvalidDataException)
                {
                    mismatchedArtifactsCount++;
                }

                var topLevelArtifacts = new[]
                {
                    Path.GetFileName(outputPath!),
                    Path.GetFileName(reportPath!),
                    Path.GetFileName(packagePath!)
                };

                mismatchedArtifactsCount += topLevelArtifacts.Count(name => string.IsNullOrWhiteSpace(name));
            }
        }

        var isConsistent = missingFilesCount == 0 && missingZipEntriesCount == 0 && mismatchedArtifactsCount == 0;
        return Task.FromResult(new PublishIntegritySummaryDto(isConsistent, missingFilesCount, missingZipEntriesCount, mismatchedArtifactsCount));
    }

    private static IReadOnlyCollection<string> ReadDocumentReferences(string outputPath)
    {
        var document = XDocument.Load(outputPath);
        var xlink = XNamespace.Get("http://www.w3.org/1999/xlink");

        return document
            .Descendants()
            .Attributes(xlink + "href")
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
