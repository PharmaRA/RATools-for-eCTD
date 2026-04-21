using System.Xml;
using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Application.Documents;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Applications;

public sealed class ApplicationImportService(
    IApplicationRepository applicationRepository,
    IDocumentRepository documentRepository,
    IDocumentPlacementRepository placementRepository) : IApplicationImportService
{
    public async Task<ApplicationImportResultDto> ImportAsync(ImportApplicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SponsorName);

        var workingDirectoryPath = Path.GetFullPath(request.WorkingDirectoryPath);
        var applicationNumber = Path.GetFileName(workingDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var existingApplications = await applicationRepository.ListAsync(cancellationToken);
        if (existingApplications.Any(x => x.ApplicationNumber == applicationNumber || string.Equals(x.WorkingDirectoryPath, workingDirectoryPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApplicationImportConflictException($"Application '{applicationNumber}' or working directory '{workingDirectoryPath}' has already been imported.");
        }

        if (!Directory.Exists(workingDirectoryPath))
        {
            throw new InvalidOperationException($"WORKING_DIRECTORY_NOT_FOUND: Working directory '{workingDirectoryPath}' does not exist.");
        }

        var application = new SubmissionApplication(applicationNumber, request.Region, request.SponsorName, workingDirectoryPath);
        var issues = new List<ApplicationImportIssueDto>();
        var importedDocuments = new Dictionary<string, SubmissionDocument>(StringComparer.OrdinalIgnoreCase);
        var importedPlacements = new List<DocumentPlacement>();

        string[] sequenceDirectories;
        try
        {
            sequenceDirectories = Directory.GetDirectories(workingDirectoryPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"WORKING_DIRECTORY_ACCESS_DENIED: Unable to access working directory '{workingDirectoryPath}'.", exception);
        }

        foreach (var sequenceDirectory in sequenceDirectories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var sequenceNumber = Path.GetFileName(sequenceDirectory);
            if (!IsSequenceDirectory(sequenceNumber))
            {
                continue;
            }

            var indexXmlPath = Path.Combine(sequenceDirectory, "index.xml");
            if (!File.Exists(indexXmlPath))
            {
                issues.Add(new ApplicationImportIssueDto("Warning", "SEQUENCE_INDEX_MISSING", sequenceNumber, $"Sequence directory '{sequenceNumber}' does not contain index.xml and was skipped."));
                continue;
            }

            var parsed = await TryImportSequenceAsync(application, sequenceNumber, indexXmlPath, importedDocuments, importedPlacements, cancellationToken);
            if (parsed is not null)
            {
                application.CreateSequence(sequenceNumber, "imported", $"Imported from {sequenceNumber}/index.xml");
                issues.AddRange(parsed.Issues);
            }
        }

        await applicationRepository.AddAsync(application, cancellationToken);

        foreach (var document in importedDocuments.Values)
        {
            await documentRepository.AddAsync(document, cancellationToken);
        }

        foreach (var placement in importedPlacements)
        {
            await placementRepository.AddAsync(placement, cancellationToken);
        }

        var importedSequenceCount = application.Sequences.Count;
        var skippedSequenceCount = issues.Count(x => x.Code == "SEQUENCE_INDEX_MISSING");
        var failedSequenceCount = issues.Count(x => x.Severity == "Error");

        return new ApplicationImportResultDto(
            application.Id,
            application.ApplicationNumber,
            application.WorkingDirectoryPath,
            importedSequenceCount,
            importedDocuments.Count,
            importedPlacements.Count,
            skippedSequenceCount,
            failedSequenceCount,
            issues);
    }

    private static async Task<SequenceImportResult?> TryImportSequenceAsync(
        SubmissionApplication application,
        string sequenceNumber,
        string indexXmlPath,
        Dictionary<string, SubmissionDocument> importedDocuments,
        List<DocumentPlacement> importedPlacements,
        CancellationToken cancellationToken)
    {
        var issues = new List<ApplicationImportIssueDto>();

        try
        {
            var xml = await LoadXmlAsync(indexXmlPath, cancellationToken);
            var sequenceRoot = Path.GetDirectoryName(indexXmlPath)!;

            foreach (var leaf in xml.Descendants().Where(x => x.Name.LocalName == "leaf"))
            {
                var href = leaf.Attributes().FirstOrDefault(x => x.Name.LocalName == "href")?.Value;
                if (string.IsNullOrWhiteSpace(href))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_INDEX_INVALID", sequenceNumber, "Leaf is missing xlink:href."));
                    return new SequenceImportResult(issues);
                }

                var resolvedPath = Path.GetFullPath(Path.Combine(sequenceRoot, href.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolvedPath))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_FILE_MISSING", sequenceNumber, $"File '{href}' referenced by index.xml was not found."));
                    return new SequenceImportResult(issues);
                }

                var checksum = leaf.Attribute("checksum")?.Value ?? ComputeMd5(resolvedPath);
                var actualChecksum = ComputeMd5(resolvedPath);
                if (!string.Equals(checksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_CHECKSUM_MISMATCH", sequenceNumber, $"File '{href}' checksum does not match index.xml."));
                    return new SequenceImportResult(issues);
                }

                if (!importedDocuments.TryGetValue(resolvedPath, out var document))
                {
                    document = new SubmissionDocument(
                        Path.GetFileName(resolvedPath),
                        GuessMediaType(resolvedPath),
                        new FileInfo(resolvedPath).Length,
                        checksum,
                        resolvedPath);

                    importedDocuments[resolvedPath] = document;
                }

                var placement = new DocumentPlacement(
                    document.Id,
                    application.Id,
                    sequenceNumber,
                    ExtractSectionPath(leaf.Parent?.Name.LocalName ?? string.Empty),
                    ParseOperation(leaf.Attribute("operation")?.Value),
                    leaf.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value);

                importedPlacements.Add(placement);
            }

            return new SequenceImportResult(issues);
        }
        catch (XmlException exception)
        {
            issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_INDEX_INVALID", sequenceNumber, exception.Message));
            return new SequenceImportResult(issues);
        }
    }

    private static async Task<XDocument> LoadXmlAsync(string path, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            Async = true
        };

        await using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    private static bool IsSequenceDirectory(string name) => name.Length == 4 && name.All(char.IsDigit);

    private static string ExtractSectionPath(string elementName)
    {
        if (string.IsNullOrWhiteSpace(elementName))
        {
            throw new XmlException("Leaf parent section element is missing.");
        }

        var tokens = elementName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pathParts = new List<string>();

        foreach (var token in tokens)
        {
            if (pathParts.Count == 0)
            {
                if (token is not ("m1" or "m2" or "m3" or "m4" or "m5"))
                {
                    break;
                }

                pathParts.Add(token);
                continue;
            }

            if (int.TryParse(token, out _) || token is "p" or "s" or "r" or "a")
            {
                pathParts.Add(token);
                continue;
            }

            break;
        }

        if (pathParts.Count == 0)
        {
            throw new XmlException($"Unable to derive CTD section path from element '{elementName}'.");
        }

        return string.Join('.', pathParts);
    }

    private static DocumentPlacementOperation ParseOperation(string? operation)
    {
        return operation?.ToLowerInvariant() switch
        {
            "new" => DocumentPlacementOperation.New,
            "replace" => DocumentPlacementOperation.Replace,
            "delete" => DocumentPlacementOperation.Delete,
            "append" => DocumentPlacementOperation.Append,
            _ => throw new XmlException($"Unsupported leaf operation '{operation}'.")
        };
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string GuessMediaType(string path)
    {
        return EctdDocumentFileRules.GetMediaType(path);
    }

    private sealed record SequenceImportResult(IReadOnlyCollection<ApplicationImportIssueDto> Issues);
}
