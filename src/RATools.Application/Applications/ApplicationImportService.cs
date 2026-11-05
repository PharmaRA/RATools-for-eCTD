using System.Xml;
using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Applications.Requests;
using RATools.Application.Documents;
using RATools.Application.Ctd;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Common;
using RATools.Domain.Documents;
using RATools.Domain.Ctd;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;
using System.Text.Json;

namespace RATools.Application.Applications;

public sealed class ApplicationImportService(
    IApplicationRepository applicationRepository,
    IApplicationImportStore importStore,
    IWorkspacePathPolicy workspacePathPolicy,
    StandardsSnapshotService snapshots) : IApplicationImportService
{
    private static readonly SectionDictionaryProfile EuSections = EuEctd322.ToProfile();
    private static readonly JsonSerializerOptions ImportJsonOptions = new(JsonSerializerDefaults.Web);

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
        var graph = new CtdNodeGraph(application.Id, IchSectionDefinitions.Current);
        var importedNodes = new List<SequenceNode>();
        var importedBackbones = new List<ImportedBackbone>();
        var regionalRevisions = new List<RegionalMetadataRevision>();
        var regionalEvidence = new List<ImportedRegionalEvidence>();
        var evidenceBudget = new ImportEvidenceBudget();
        var snapshot = await snapshots.CaptureAsync(template.Key, cancellationToken: cancellationToken);

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
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedSequenceDirectory = workspacePathPolicy.EnsureAllowed(sequenceDirectory);
            var sequenceNumber = Path.GetFileName(normalizedSequenceDirectory);
            if (!IsSequenceDirectory(sequenceNumber))
            {
                continue;
            }
            evidenceBudget.Sequence();

            var indexXmlPath = workspacePathPolicy.EnsureAllowed(Path.Combine(normalizedSequenceDirectory, "index.xml"));
            if (!File.Exists(indexXmlPath))
            {
                issues.Add(new ApplicationImportIssueDto("Warning", "SEQUENCE_INDEX_MISSING", sequenceNumber, $"Sequence directory '{sequenceNumber}' does not contain index.xml and was skipped."));
                continue;
            }

            var parsed = await TryImportSequenceAsync(application, sequenceNumber, indexXmlPath, workspacePathPolicy, fileHashes, importedDocuments, importedPlacements, importedLeafIndex, graph, snapshot, evidenceBudget, cancellationToken);
            if (parsed is not null)
            {
                if (parsed.Issues.All(issue => issue.Severity != "Error"))
                {
                    var sequence = application.CreateSequence(sequenceNumber, "imported", $"Imported from {sequenceNumber}/index.xml");
                    if (parsed.PublishingMetadata is not null)
                    {
                        sequence.RevisePublishingMetadata(parsed.PublishingMetadata);
                    }
                    graph = parsed.Tree!.Graph;
                    importedNodes.AddRange(parsed.Tree.Nodes);
                    importedBackbones.AddRange(parsed.Backbones!);
                    if (parsed.RegionalRevision is { } revision) regionalRevisions.Add(revision);
                    regionalEvidence.AddRange(parsed.RegionalEvidence ?? []);
                }
                issues.AddRange(parsed.Issues);
            }
        }

        // Later sequences can establish that a previously unique identity was
        // ambiguous. Recompute statuses against the final, still-unpersisted graph.
        var finalNodes = importedNodes.Select(node => new SequenceNode(graph, node.SequenceNumber, node.NodeInstanceId,
            node.Attributes, node.Title, node.SortOrder, node.StorageSegment)).ToArray();
        await importStore.SaveAsync(new ApplicationImportBatch(application, graph, finalNodes,
            importedDocuments.Values, importedPlacements, importedBackbones, regionalRevisions, regionalEvidence), cancellationToken);

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
        CtdNodeGraph previousGraph,
        StandardsProfileSnapshot snapshot,
        ImportEvidenceBudget evidenceBudget,
        CancellationToken cancellationToken)
    {
        var issues = new List<ApplicationImportIssueDto>();
        var sequenceDocuments = new Dictionary<string, SubmissionDocument>(StringComparer.OrdinalIgnoreCase);
        var sequenceLeaves = new List<(string SourcePath, string? Href, DocumentPlacement Placement, SubmissionDocument Document, string? Context)>();
        var contexts = new List<RegionalDocumentContext>();

        try
        {
            var indexBytes = await ReadXmlBytesAsync(indexXmlPath, evidenceBudget, cancellationToken);
            var xml = LoadXml(indexBytes);
            var sequenceRoot = Path.GetDirectoryName(indexXmlPath)!;
            var isEu = application.EctdTemplateKey == EctdTemplateRegistry.EuTemplateKey;
            var profile = isEu ? BackboneXmlProfiles.EuEctd322Regional : BackboneXmlProfiles.FdaEctd322UsRegional33;
            var sections = isEu ? EuSections : SectionDictionaryProfiles.FdaEctd32;
            var regionalPath = workspacePathPolicy.EnsureAllowed(Path.Combine(sequenceRoot, profile.Regional.RelativePath!));
            var regionalReferences = xml.Root?.Elements().Where(element => element.Name.LocalName == "m1-administrative-information-and-prescribing-information")
                .Elements().Where(element => element.Name.LocalName == "leaf")
                .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value)
                .Where(href => href?.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) == true).ToArray() ?? [];
            if (regionalReferences.Length > 1) throw new XmlException("Multiple regional backbones require a region-specific import profile.");
            if (regionalReferences.Length == 1)
            {
                if (!TryResolveLeafPath(indexXmlPath, regionalReferences[0]!, sequenceRoot, out var referencedPath))
                    throw new XmlException("The regional backbone reference is outside this sequence or is not a local file URI.");
                regionalPath = workspacePathPolicy.EnsureAllowed(referencedPath!);
            }
            var sources = new List<(string Path, XDocument Xml, byte[] Bytes)> { (indexXmlPath, xml, indexBytes) };
            SequencePublishingMetadata? publishingMetadata = null;
            RegionalImportParseResult? regional = null;
            if (File.Exists(regionalPath))
            {
                var regionalBytes = await ReadXmlBytesAsync(regionalPath, evidenceBudget, cancellationToken);
                var regionalXml = LoadXml(regionalBytes);
                if (regionalXml.Root?.Name.LocalName != profile.Regional.RootElementName)
                    throw new XmlException($"Regional backbone '{Path.GetRelativePath(sequenceRoot, regionalPath)}' has an unexpected root element.");
                sources.Add((regionalPath, regionalXml, regionalBytes));
                regional = RegionalImportParser.Parse(regionalBytes, isEu, sequenceNumber, snapshot);
                publishingMetadata = CompatibilityMetadata(regional.Content);
            }
            else
            {
                issues.Add(new ApplicationImportIssueDto("Warning", "SEQUENCE_REGIONAL_MISSING", sequenceNumber,
                    $"Regional backbone '{Path.GetRelativePath(sequenceRoot, regionalPath)}' was not found; regional documents and metadata could not be imported."));
            }

            foreach (var source in sources)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in source.Xml.Descendants().Attributes("ID"))
                    if (!ids.Add(id.Value)) throw new XmlException($"Duplicate XML ID '{id.Value}' in '{Path.GetFileName(source.Path)}'.");
            }
            var tree = new ImportedNodeTree(previousGraph, sequenceNumber, importedLeafIndex, issues);
            tree.Read(xml, indexXmlPath, cancellationToken);
            foreach (var (sourcePath, leaf) in sources.SelectMany(source => source.Xml.Descendants()
                .Where(element => element.Name.LocalName == "leaf")
                .Select(element => (source.Path, Leaf: element))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = ParseOperation(leaf.Attribute("operation")?.Value);
                var node = tree.ParentOf(leaf);
                var section = node?.CtdSection ?? ExtractSectionPath(leaf, sections, isEu);
                var backbonePath = Path.GetRelativePath(sequenceRoot, sourcePath).Replace('\\', '/');
                var context = node is null ? ImportedNodeTree.UnboundContext(leaf) : null;
                var sortOrder = leaf.ElementsBeforeSelf().Count(element => element.Name.LocalName != "title");
                var leafId = leaf.Attribute("ID")?.Value;

                var href = leaf.Attributes().FirstOrDefault(x => x.Name.LocalName == "href")?.Value;
                var modifiedFile = leaf.Attribute("modified-file")?.Value;
                var source = new ImportedLeafSource(backbonePath, href, modifiedFile);
                void Bind(DocumentPlacement item)
                {
                    item.PreserveImportedSource(source, sortOrder);
                    if (node is not null) item.BindToNode(node, sortOrder);
                    else issues.Add(new ApplicationImportIssueDto("Warning", "NODE_SCHEMA_NOT_AVAILABLE", sequenceNumber,
                        $"Leaf '{item.LeafId}' in '{source.BackboneRelativePath}' retains section '{section}' and archived XML context; a compatible node schema is required to bind it."));
                    if (regional is not null) contexts.Add(RegionalImportParser.Context(leaf, item.Id, node?.NodeInstanceId));
                }
                ImportedLeaf? target = null;
                if (operation != DocumentPlacementOperation.New)
                {
                    if (!string.IsNullOrWhiteSpace(modifiedFile))
                    {
                        target = importedLeafIndex.Resolve(sourcePath, modifiedFile, sequenceNumber, section,
                            node?.NodeInstanceId, context, node is not null && tree.Graph.HasUnresolvedIdentity(node.NodeInstanceId), backbonePath);
                    }
                    if (target is null)
                    {
                        var missing = string.IsNullOrWhiteSpace(modifiedFile);
                        var fatal = node is not null || operation == DocumentPlacementOperation.Delete;
                        issues.Add(new ApplicationImportIssueDto(fatal ? "Error" : "Warning",
                            missing ? "LIFECYCLE_TARGET_MISSING" : "LIFECYCLE_TARGET_NOT_IMPORTED", sequenceNumber,
                            missing ? $"Lifecycle leaf '{href ?? leafId}' is missing modified-file."
                                : $"Lifecycle leaf '{href ?? leafId}' references modified-file '{modifiedFile}', but no unique imported historical leaf matched it."));
                        if (fatal)
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
                    Bind(deletion);
                    sequenceLeaves.Add((sourcePath, null, deletion, target.Document, context));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(href))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_INDEX_INVALID", sequenceNumber, "Leaf is missing xlink:href."));
                    return new SequenceImportResult(issues);
                }

                if (!TryResolveLeafPath(sourcePath, href, sequenceRoot, out var resolvedPath))
                {
                    issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_FILE_OUTSIDE_WORKSPACE", sequenceNumber, $"File '{href}' resolves outside the sequence workspace."));
                    return new SequenceImportResult(issues);
                }

                var leafParentPath = Path.GetDirectoryName(resolvedPath!)!;
                workspacePathPolicy.EnsureAllowed(leafParentPath);
                resolvedPath = workspacePathPolicy.EnsureAllowed(resolvedPath!);

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
                Bind(placement);
                sequenceLeaves.Add((sourcePath, href, placement, document, context));
            }

            RegionalMetadataRevision? importedRevision = null;
            ImportedRegionalEvidence[] evidence = [];
            if (regional is not null)
            {
                var metadataId = Guid.NewGuid();
                evidence = sources.Select(source => new ImportedRegionalEvidence(Guid.NewGuid(), application.Id, sequenceNumber, metadataId,
                    snapshot.Id, Path.GetRelativePath(sequenceRoot, source.Path).Replace('\\', '/'), source.Bytes, ImportedRegionalEvidence.Hash(source.Bytes))).ToArray();
                var sourceEvidence = evidence.Single(item => item.RelativePath == Path.GetRelativePath(sequenceRoot, regionalPath).Replace('\\', '/'));
                var nodeContexts = tree.RegionalContexts.Select(item =>
                {
                    var context = RegionalImportParser.Context(item.Element, item.Id, item.Id);
                    return new RegionalNodeContext(item.Id, context.Country, context.Language, context.ProductInformationType, context.FormType);
                }).Where(item => item.Country is not null || item.Language is not null || item.ProductInformationType is not null || item.FormType is not null).ToArray();
                var diagnostics = regional.Diagnostics.Concat(contexts.Where(item => item.NodeInstanceId is null).Select(item =>
                    new RegionalMetadataDiagnostic("RegionalContextUnbound", $"documentContexts[{contexts.IndexOf(item)}]", "The archived regional placement has no compatible imported node; its exact context remains unbound."))).ToArray();
                var provenance = ImportedProvenance(regional.Content, [], [], sourceEvidence);
                foreach (var (context, ordinal) in contexts.Select((context, ordinal) => (context, ordinal)))
                {
                    var placement = sequenceLeaves.Single(item => item.Placement.Id == context.PlacementId);
                    var contextEvidence = evidence.Single(item => item.RelativePath == Path.GetRelativePath(sequenceRoot, placement.SourcePath).Replace('\\', '/'));
                    AddContextProvenance(provenance, $"documentContexts[{ordinal}]", context.Country, context.Language, context.ProductInformationType, context.FormType, contextEvidence);
                }
                var indexEvidence = evidence.Single(item => item.RelativePath == "index.xml");
                foreach (var (context, ordinal) in nodeContexts.Select((context, ordinal) => (context, ordinal)))
                    AddContextProvenance(provenance, $"nodeContexts[{ordinal}]", context.Country, context.Language, context.ProductInformationType, context.FormType, indexEvidence);
                importedRevision = RegionalMetadataRevision.Create(metadataId, application.Id, sequenceNumber, 1, 1, snapshot.Id, regional.Content, null,
                    nodeContexts, contexts, null, null, provenance, diagnostics, null, null, DateTime.UtcNow);
                issues.AddRange(diagnostics.Select(item => new ApplicationImportIssueDto("Warning", item.Code, sequenceNumber, item.Message, item.FieldPath)));
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
                importedLeafIndex.Add(entry.SourcePath, entry.Href, entry.Placement, entry.Document, entry.Context);
            }

            return new SequenceImportResult(issues, publishingMetadata, tree,
                sources.Select(source => new ImportedBackbone(sequenceNumber,
                    Path.GetRelativePath(sequenceRoot, source.Path).Replace('\\', '/'), source.Xml.ToString(SaveOptions.DisableFormatting))).ToArray(), importedRevision, evidence);
        }
        catch (Exception exception) when (exception is XmlException or CtdNodeConstraintException or ArgumentException)
        {
            issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_INDEX_INVALID", sequenceNumber, exception.Message));
            return new SequenceImportResult(issues);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new ApplicationImportIssueDto("Error", "SEQUENCE_READ_FAILED", sequenceNumber, exception.Message));
            return new SequenceImportResult(issues);
        }
    }

    private static async Task<byte[]> ReadXmlBytesAsync(string path, ImportEvidenceBudget budget, CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(path);
        if (source.Length is < 1 or > 8 * 1024 * 1024) throw new XmlException("XML evidence exceeds the 8 MiB input bound.");
        var reserved = source.Length;
        budget.Source(reserved);
        using var target = new MemoryStream(); var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var length = target.Length + count;
            if (length > 8 * 1024 * 1024) throw new XmlException("XML evidence grew beyond its input bound.");
            if (length > reserved) { budget.Bytes(length - reserved); reserved = length; }
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return target.ToArray();
    }
    private static XDocument LoadXml(byte[] bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        };

        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static bool IsSequenceDirectory(string name) => name.Length == 4 && name.All(character => character is >= '0' and <= '9');

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

    private static bool TryResolveLeafPath(string sourcePath, string href, string sequenceRoot, out string? path)
    {
        path = null;
        var source = new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = sourcePath }.Uri;
        if (href != href.Trim() || !Uri.TryCreate(source, href.Replace('\\', '/'), out var uri) ||
            !uri.IsFile || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
        path = Path.GetFullPath(uri.LocalPath);
        return WorkspacePathGuard.IsInsideScope(path, sequenceRoot);
    }

    private static SequencePublishingMetadata? CompatibilityMetadata(RegionalMetadataContent content)
    {
        var us = content.Us; var eu = content.Eu is { Envelopes.Count: > 0 } european ? european.Envelopes[0] : null;
        var contact = us is { Contacts.Count: > 0 } american ? american.Contacts[0] : null;
        var phone = contact is { Phones.Count: > 0 } phones ? phones.Phones[0] : null;
        var email = contact is { Emails.Count: > 0 } emails ? emails.Emails[0] : null;
        var description = us?.Description ?? eu?.Description; var applicant = us?.ApplicantName ?? eu?.Applicant;
        var submission = us?.SubmissionType ?? eu?.SubmissionType;
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(applicant) || string.IsNullOrWhiteSpace(submission)) return null;
        return SequencePublishingMetadata.Create(
            us?.ApplicationType, submission, us?.SubmissionSubtype ?? eu?.SubmissionUnit, description, applicant, us?.FormType,
            contact?.Name, contact?.Type, phone?.Number, phone?.Type, email?.Value);
    }

    private static List<RegionalMetadataProvenance> ImportedProvenance(RegionalMetadataContent content, IReadOnlyList<RegionalNodeContext> nodes, IReadOnlyList<RegionalDocumentContext> contexts, ImportedRegionalEvidence evidence)
    {
        var provenance = new List<RegionalMetadataProvenance>();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { content.Us, content.Eu, NodeContexts = nodes, DocumentContexts = contexts }, ImportJsonOptions));
        void Walk(JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) Walk(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name);
            else if (value.ValueKind == JsonValueKind.Array) { var index = 0; foreach (var item in value.EnumerateArray()) Walk(item, path + "[" + index++ + "]"); }
            else if (value.ValueKind != JsonValueKind.Null) provenance.Add(new(path, "Imported", evidence.Id, evidence.Sha256));
        }
        Walk(document.RootElement, ""); return provenance;
    }
    private static void AddContextProvenance(List<RegionalMetadataProvenance> provenance, string path, string? country, string? language, string? piType, string? formType, ImportedRegionalEvidence evidence)
    {
        foreach (var (field, value) in new[] { ("country", country), ("language", language), ("productInformationType", piType), ("formType", formType) })
            if (value is not null) provenance.Add(new(path + "." + field, "Imported", evidence.Id, evidence.Sha256));
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
        SequencePublishingMetadata? PublishingMetadata = null,
        ImportedNodeTree? Tree = null,
        IReadOnlyList<ImportedBackbone>? Backbones = null,
        RegionalMetadataRevision? RegionalRevision = null,
        IReadOnlyList<ImportedRegionalEvidence>? RegionalEvidence = null);
}
