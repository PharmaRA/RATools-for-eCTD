using System.Xml;
using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Applications.Requests;
using RATools.Application.Documents;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Applications;

public sealed class ApplicationImportService(
    IApplicationRepository applicationRepository,
    IDocumentRepository documentRepository,
    IDocumentPlacementRepository placementRepository,
    IWorkspacePathPolicy workspacePathPolicy) : IApplicationImportService
{
    public async Task<ApplicationImportResultDto> ImportAsync(ImportApplicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EctdTemplateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SponsorName);

        var template = EctdTemplateRegistry.Resolve(request.EctdTemplateKey);

        var workingDirectoryPath = workspacePathPolicy.EnsureAllowed(request.WorkingDirectoryPath);
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

        var application = new SubmissionApplication(applicationNumber, template.Region, request.SponsorName, workingDirectoryPath, template.Key);
        var issues = new List<ApplicationImportIssueDto>();
        var importedDocuments = new Dictionary<string, SubmissionDocument>(StringComparer.OrdinalIgnoreCase);
        var importedPlacements = new List<DocumentPlacement>();
        var importedPlacementByHref = new Dictionary<string, DocumentPlacement>(StringComparer.Ordinal);
        var fileHashes = new ImportFileHashCache();

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
            var normalizedSequenceDirectory = workspacePathPolicy.EnsureAllowed(sequenceDirectory);
            var sequenceNumber = Path.GetFileName(normalizedSequenceDirectory);
            if (!IsSequenceDirectory(sequenceNumber))
            {
                continue;
            }

            var indexXmlPath = workspacePathPolicy.EnsureAllowed(Path.Combine(normalizedSequenceDirectory, "index.xml"));
            if (!File.Exists(indexXmlPath))
            {
                issues.Add(new ApplicationImportIssueDto("Warning", "SEQUENCE_INDEX_MISSING", sequenceNumber, $"Sequence directory '{sequenceNumber}' does not contain index.xml and was skipped."));
                continue;
            }

            var parsed = await TryImportSequenceAsync(application, sequenceNumber, indexXmlPath, workspacePathPolicy, fileHashes, importedDocuments, importedPlacements, importedPlacementByHref, cancellationToken);
            if (parsed is not null)
            {
                application.CreateSequence(sequenceNumber, "imported", $"Imported from {sequenceNumber}/index.xml");
                issues.AddRange(parsed.Issues);
            }
        }

        var persistedDocumentIds = new List<Guid>();
        var persistedPlacementIds = new List<Guid>();

        try
        {
            await applicationRepository.AddAsync(application, cancellationToken);

            foreach (var document in importedDocuments.Values)
            {
                await documentRepository.AddAsync(document, cancellationToken);
                persistedDocumentIds.Add(document.Id);
            }

            foreach (var placement in importedPlacements)
            {
                await placementRepository.AddAsync(placement, cancellationToken);
                persistedPlacementIds.Add(placement.Id);
            }
        }
        catch
        {
            foreach (var placementId in persistedPlacementIds)
            {
                await placementRepository.DeleteAsync(placementId, cancellationToken);
            }

            foreach (var documentId in persistedDocumentIds)
            {
                await documentRepository.DeleteAsync(documentId, cancellationToken);
            }

            await applicationRepository.DeleteAsync(application.Id, cancellationToken);
            throw;
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
        IWorkspacePathPolicy workspacePathPolicy,
        ImportFileHashCache fileHashes,
        Dictionary<string, SubmissionDocument> importedDocuments,
        List<DocumentPlacement> importedPlacements,
        Dictionary<string, DocumentPlacement> importedPlacementByHref,
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
                if (!WorkspacePathGuard.IsInsideScope(resolvedPath, sequenceRoot))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_FILE_OUTSIDE_WORKSPACE", sequenceNumber, $"File '{href}' resolves outside the sequence workspace."));
                    return new SequenceImportResult(issues);
                }

                var leafParentPath = Path.GetDirectoryName(resolvedPath)!;
                workspacePathPolicy.EnsureAllowed(leafParentPath);
                resolvedPath = workspacePathPolicy.EnsureAllowed(resolvedPath);

                if (!File.Exists(resolvedPath))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_FILE_MISSING", sequenceNumber, $"File '{href}' referenced by index.xml was not found."));
                    return new SequenceImportResult(issues);
                }

                var hashes = await fileHashes.GetAsync(resolvedPath, cancellationToken);
                var checksum = leaf.Attribute("checksum")?.Value ?? hashes.Md5;
                if (!string.Equals(checksum, hashes.Md5, StringComparison.OrdinalIgnoreCase))
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
                        hashes.Sha256,
                        hashes.Md5,
                        resolvedPath);

                    importedDocuments[resolvedPath] = document;
                }

                var operation = ParseOperation(leaf.Attribute("operation")?.Value);
                var placement = new DocumentPlacement(
                    document.Id,
                    application.Id,
                    sequenceNumber,
                    ExtractSectionPath(leaf.Parent?.Name.LocalName ?? string.Empty),
                    operation,
                    leaf.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value);

                if (operation is DocumentPlacementOperation.Replace or DocumentPlacementOperation.Delete or DocumentPlacementOperation.Append)
                {
                    var modifiedFile = leaf.Attribute("modified-file")?.Value;
                    if (string.IsNullOrWhiteSpace(modifiedFile))
                    {
                        issues.Add(new ApplicationImportIssueDto("Warning", "LIFECYCLE_TARGET_MISSING", sequenceNumber, $"Lifecycle leaf '{href}' is missing modified-file."));
                    }
                    else if (importedPlacementByHref.TryGetValue(NormalizeLeafHref(modifiedFile), out var targetPlacement)
                        && CompareSequenceNumbers(targetPlacement.SequenceNumber, sequenceNumber) < 0)
                    {
                        placement.ReviseLifecycleTarget(targetPlacement.Id);
                    }
                    else
                    {
                        issues.Add(new ApplicationImportIssueDto("Warning", "LIFECYCLE_TARGET_NOT_IMPORTED", sequenceNumber, $"Lifecycle leaf '{href}' references modified-file '{modifiedFile}', but no imported historical leaf matched it."));
                    }
                }

                importedPlacements.Add(placement);
                importedPlacementByHref[NormalizeLeafHref(href)] = placement;
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

    private static string NormalizeLeafHref(string href)
    {
        var normalized = href.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static int CompareSequenceNumbers(string left, string right)
    {
        if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static string GuessMediaType(string path)
    {
        return EctdDocumentFileRules.GetMediaType(path);
    }

    private sealed record SequenceImportResult(IReadOnlyCollection<ApplicationImportIssueDto> Issues);
}
