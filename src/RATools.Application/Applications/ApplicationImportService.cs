using System.Xml;
using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Applications.Requests;
using RATools.Application.Documents;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Common;
using RATools.Domain.Documents;

namespace RATools.Application.Applications;

public sealed class ApplicationImportService(
    IApplicationRepository applicationRepository,
    IDocumentRepository documentRepository,
    IDocumentPlacementRepository placementRepository,
    IWorkspacePathPolicy workspacePathPolicy) : IApplicationImportService
{
    private static readonly SectionDictionaryProfile EuSections = EuEctd322.ToProfile();

    public async Task<ApplicationImportResultDto> ImportAsync(ImportApplicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EctdTemplateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SponsorName);

        var template = EctdTemplateRegistry.Resolve(request.EctdTemplateKey);

        var workingDirectoryPath = workspacePathPolicy.EnsureAllowed(request.WorkingDirectoryPath);
        var applicationNumber = PortablePathSegment.NormalizeAndValidate(
            Path.GetFileName(workingDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            "applicationNumber");
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
        var importedLeafIndex = new ImportedLeafIndex(workingDirectoryPath);
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

            var parsed = await TryImportSequenceAsync(application, sequenceNumber, indexXmlPath, workspacePathPolicy, fileHashes, importedDocuments, importedPlacements, importedLeafIndex, cancellationToken);
            if (parsed is not null)
            {
                if (parsed.Issues.All(issue => issue.Severity != "Error"))
                {
                    var sequence = application.CreateSequence(sequenceNumber, "imported", $"Imported from {sequenceNumber}/index.xml");
                    if (parsed.PublishingMetadata is not null)
                    {
                        sequence.RevisePublishingMetadata(parsed.PublishingMetadata);
                    }
                }
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
        var issueSummary = ApplicationImportIssueSummary.Create(issues);

        return new ApplicationImportResultDto(
            application.Id,
            application.ApplicationNumber,
            application.WorkingDirectoryPath,
            importedSequenceCount,
            importedDocuments.Count,
            importedPlacements.Count,
            issueSummary.SkippedSequenceCount,
            issueSummary.FailedSequenceCount,
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
        ImportedLeafIndex importedLeafIndex,
        CancellationToken cancellationToken)
    {
        var issues = new List<ApplicationImportIssueDto>();
        var sequenceDocuments = new Dictionary<string, SubmissionDocument>(StringComparer.OrdinalIgnoreCase);
        var sequenceLeaves = new List<(string SourcePath, string? Href, DocumentPlacement Placement, SubmissionDocument Document)>();
        var leafIds = new HashSet<(string SourcePath, string LeafId)>();

        try
        {
            var xml = await LoadXmlAsync(indexXmlPath, cancellationToken);
            var sequenceRoot = Path.GetDirectoryName(indexXmlPath)!;
            var isEu = application.EctdTemplateKey == EctdTemplateRegistry.EuTemplateKey;
            var profile = isEu ? BackboneXmlProfiles.EuEctd322Regional : BackboneXmlProfiles.FdaEctd322UsRegional33;
            var sections = isEu ? EuSections : SectionDictionaryProfiles.FdaEctd32;
            var regionalPath = workspacePathPolicy.EnsureAllowed(Path.Combine(sequenceRoot, profile.Regional.RelativePath!));
            var sources = new List<(string Path, XDocument Xml)> { (indexXmlPath, xml) };
            SequencePublishingMetadata? publishingMetadata = null;
            if (File.Exists(regionalPath))
            {
                var regionalXml = await LoadXmlAsync(regionalPath, cancellationToken);
                sources.Add((regionalPath, regionalXml));
                publishingMetadata = ReadPublishingMetadata(regionalXml, isEu, application.SponsorName, sequenceNumber);
            }
            else
            {
                issues.Add(new ApplicationImportIssueDto("Warning", "SEQUENCE_REGIONAL_MISSING", sequenceNumber,
                    $"Regional backbone '{profile.Regional.RelativePath}' was not found; regional documents and metadata could not be imported."));
            }

            foreach (var (sourcePath, leaf) in sources.SelectMany(source => source.Xml.Descendants()
                .Where(element => element.Name.LocalName == "leaf")
                .Select(element => (source.Path, Leaf: element))))
            {
                var operation = ParseOperation(leaf.Attribute("operation")?.Value);
                var section = ExtractSectionPath(leaf, sections, isEu);
                var leafId = leaf.Attribute("ID")?.Value;
                if (!string.IsNullOrWhiteSpace(leafId) && !leafIds.Add((sourcePath, leafId)))
                {
                    throw new XmlException($"Duplicate leaf ID '{leafId}' in '{Path.GetFileName(sourcePath)}'.");
                }

                var href = leaf.Attributes().FirstOrDefault(x => x.Name.LocalName == "href")?.Value;
                ImportedLeaf? target = null;
                if (operation != DocumentPlacementOperation.New)
                {
                    var modifiedFile = leaf.Attribute("modified-file")?.Value;
                    if (!string.IsNullOrWhiteSpace(modifiedFile))
                    {
                        target = importedLeafIndex.Resolve(sourcePath, modifiedFile, sequenceNumber, section);
                    }
                    if (target is null)
                    {
                        var missing = string.IsNullOrWhiteSpace(modifiedFile);
                        issues.Add(new ApplicationImportIssueDto(operation == DocumentPlacementOperation.Delete ? "Error" : "Warning",
                            missing ? "LIFECYCLE_TARGET_MISSING" : "LIFECYCLE_TARGET_NOT_IMPORTED", sequenceNumber,
                            missing ? $"Lifecycle leaf '{href ?? leafId}' is missing modified-file."
                                : $"Lifecycle leaf '{href ?? leafId}' references modified-file '{modifiedFile}', but no unique imported historical leaf matched it."));
                        if (operation == DocumentPlacementOperation.Delete)
                        {
                            return new SequenceImportResult(issues);
                        }
                    }
                }

                var title = leaf.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value;
                if (operation == DocumentPlacementOperation.Delete)
                {
                    var deletion = new DocumentPlacement(target!.Document.Id, application.Id, sequenceNumber, section, operation, title, leafId);
                    deletion.ReviseLifecycleTarget(target.Placement.Id);
                    sequenceLeaves.Add((sourcePath, null, deletion, target.Document));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(href))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_INDEX_INVALID", sequenceNumber, "Leaf is missing xlink:href."));
                    return new SequenceImportResult(issues);
                }

                var resolvedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, href.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
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
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_FILE_MISSING", sequenceNumber, $"File '{href}' referenced by '{Path.GetFileName(sourcePath)}' was not found."));
                    return new SequenceImportResult(issues);
                }

                var hashes = await fileHashes.GetAsync(resolvedPath, cancellationToken);
                var checksum = leaf.Attribute("checksum")?.Value ?? hashes.Md5;
                if (!string.Equals(checksum, hashes.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_CHECKSUM_MISMATCH", sequenceNumber, $"File '{href}' checksum does not match '{Path.GetFileName(sourcePath)}'."));
                    return new SequenceImportResult(issues);
                }

                // The regional backbone can itself be referenced from index.xml.
                // Its leaves are imported from the regional source, not as an XML document.
                if (string.Equals(resolvedPath, regionalPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!sequenceDocuments.TryGetValue(resolvedPath, out var document))
                {
                    document = new SubmissionDocument(
                        Path.GetFileName(resolvedPath),
                        GuessMediaType(resolvedPath),
                        new FileInfo(resolvedPath).Length,
                        hashes.Sha256,
                        hashes.Md5,
                        resolvedPath);

                    sequenceDocuments[resolvedPath] = document;
                }

                var placement = new DocumentPlacement(
                    document.Id,
                    application.Id,
                    sequenceNumber,
                    section,
                    operation,
                    title,
                    leafId);
                placement.ReviseLifecycleTarget(target?.Placement.Id);
                sequenceLeaves.Add((sourcePath, href, placement, document));
            }

            // Publish a sequence to the import state only after every backbone
            // was parsed successfully, so failed sequences cannot become targets.
            foreach (var (path, document) in sequenceDocuments)
            {
                importedDocuments.Add(path, document);
            }
            foreach (var entry in sequenceLeaves)
            {
                importedPlacements.Add(entry.Placement);
                importedLeafIndex.Add(entry.SourcePath, entry.Href, entry.Placement, entry.Document);
            }

            return new SequenceImportResult(issues, publishingMetadata);
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

    private static string ExtractSectionPath(XElement leaf, SectionDictionaryProfile sections, bool isEu)
    {
        foreach (var ancestor in leaf.Ancestors())
        {
            var name = ancestor.Name.LocalName;
            if (!isEu && name == "form")
            {
                return "m1.1";
            }

            if (sections.ByElementName.TryGetValue(name, out var section))
            {
                return section.SectionPath;
            }

            // Retain support for earlier workspaces with short section names,
            // such as m1-1, while walking past regional wrappers like pi-doc.
            if (name.Length > 2 && name[0] == 'm' && name[1] is >= '1' and <= '5' && name[2] == '-')
            {
                return ExtractSectionPath(name);
            }
        }

        throw new XmlException("Leaf parent section element is missing.");
    }

    private static SequencePublishingMetadata? ReadPublishingMetadata(XDocument xml, bool isEu, string sponsor, string sequenceNumber)
    {
        var container = xml.Descendants().FirstOrDefault(element => element.Name.LocalName == (isEu ? "envelope" : "admin"));
        if (container is null)
        {
            return null;
        }

        XElement? Element(string name) => container.Descendants().FirstOrDefault(element => element.Name.LocalName == name);
        var description = Element("submission-description")?.Value;
        var applicant = Element(isEu ? "applicant" : "company-name")?.Value;
        var submission = Element(isEu ? "submission" : "submission-id");
        var submissionType = submission?.Attribute(isEu ? "type" : "submission-type")?.Value;
        return SequencePublishingMetadata.Create(
            isEu ? null : Element("application-number")?.Attribute("application-type")?.Value,
            string.IsNullOrWhiteSpace(submissionType) ? "imported" : submissionType,
            isEu ? Element("submission-unit")?.Attribute("type")?.Value : Element("sequence-number")?.Attribute("submission-sub-type")?.Value,
            string.IsNullOrWhiteSpace(description) ? $"Imported sequence {sequenceNumber}" : description,
            string.IsNullOrWhiteSpace(applicant) ? sponsor : applicant,
            isEu ? null : Element("form")?.Attribute("form-type")?.Value,
            Element("applicant-contact-name")?.Value,
            Element("applicant-contact-name")?.Attribute("applicant-contact-type")?.Value,
            Element("telephone")?.Value,
            Element("telephone")?.Attribute("telephone-number-type")?.Value,
            Element("email")?.Value);
    }

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

    private static string GuessMediaType(string path)
    {
        return EctdDocumentFileRules.GetMediaType(path);
    }

    private sealed record SequenceImportResult(
        IReadOnlyCollection<ApplicationImportIssueDto> Issues,
        SequencePublishingMetadata? PublishingMetadata = null);
}
