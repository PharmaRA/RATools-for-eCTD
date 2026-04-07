using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Publishing;

public sealed class BackboneService(
    IApplicationRepository applicationRepository,
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IBackboneFileWriter backboneFileWriter) : IBackboneService
{
    private static readonly XNamespace EctdNamespace = "http://example.org/ectd/backbone";
    private static readonly XNamespace XlinkNamespace = "http://www.w3.org/1999/xlink";

    public async Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            throw new InvalidOperationException($"Application {request.ApplicationId} was not found.");
        }

        var sequence = application.Sequences.SingleOrDefault(x => x.SequenceNumber == request.SequenceNumber);
        if (sequence is null)
        {
            throw new InvalidOperationException($"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}.");
        }

        var placements = await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
        var documents = await documentRepository.ListAsync(cancellationToken);
        var documentById = documents.ToDictionary(x => x.Id, x => x);
        var referencedDocuments = placements
            .Select(x => x.DocumentId)
            .Distinct()
            .Where(documentById.ContainsKey)
            .Select(id => documentById[id])
            .ToArray();

        var root = new XElement(EctdNamespace + "ectd",
            new XAttribute(XNamespace.Xmlns + "ectd", EctdNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("applicationNumber", application.ApplicationNumber),
            new XAttribute("sequenceNumber", sequence.SequenceNumber),
            new XAttribute("submissionType", sequence.SubmissionType),
            new XAttribute("region", application.Region),
            new XElement(EctdNamespace + "applicant", application.SponsorName),
            new XElement(EctdNamespace + "sequenceDescription", sequence.Description),
            BuildSectionTree(placements, documentById));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        var xmlContent = document.ToString();
        var output = await backboneFileWriter.SaveAsync(
            request.ApplicationId,
            request.SequenceNumber,
            "index.xml",
            xmlContent,
            request.ReportFileName,
            request.PackageFileName,
            "{}",
            referencedDocuments,
            cancellationToken);

        return new GeneratedBackboneDto(
            request.ApplicationId,
            request.SequenceNumber,
            "index.xml",
            output.FilePath,
            output.ReportPath,
            output.PackagePath,
            xmlContent);
    }

    private static IEnumerable<XElement> BuildSectionTree(
        IReadOnlyCollection<DocumentPlacement> placements,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        var roots = new SortedDictionary<string, SectionNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var placement in placements.OrderBy(x => x.CreatedUtc))
        {
            var sectionPath = SplitSectionPath(placement.CtdSection);
            if (sectionPath.Length == 0)
            {
                continue;
            }

            var currentLevel = roots;
            SectionNode? currentNode = null;

            foreach (var part in sectionPath)
            {
                if (!currentLevel.TryGetValue(part, out currentNode))
                {
                    currentNode = new SectionNode(part);
                    currentLevel[part] = currentNode;
                }

                currentLevel = currentNode.Children;
            }

            currentNode?.Placements.Add(placement);
        }

        foreach (var root in roots.Values)
        {
            yield return BuildSectionElement(root, documentById);
        }
    }

    private static XElement BuildSectionElement(SectionNode node, IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        var element = new XElement(EctdNamespace + "section", new XAttribute("id", node.Id));

        foreach (var child in node.Children.Values)
        {
            element.Add(BuildSectionElement(child, documentById));
        }

        foreach (var placement in node.Placements.OrderBy(x => x.CreatedUtc))
        {
            if (!documentById.TryGetValue(placement.DocumentId, out var document))
            {
                continue;
            }

            element.Add(new XElement(EctdNamespace + "leaf",
                new XAttribute("id", placement.Id),
                new XAttribute("operation", placement.Operation.ToString().ToLowerInvariant()),
                new XAttribute(XlinkNamespace + "href", BuildLeafHref(document)),
                new XElement(EctdNamespace + "title", placement.Title ?? document.FileName),
                new XElement(EctdNamespace + "fileName", document.FileName),
                new XElement(EctdNamespace + "mimeType", document.MediaType),
                new XElement(EctdNamespace + "checksum", document.Sha256)));
        }

        return element;
    }

    private static string BuildLeafHref(SubmissionDocument document)
    {
        return PublishOutputNaming.BuildPublishedDocumentRelativePath(document);
    }

    private static string[] SplitSectionPath(string ctdSection)
    {
        return ctdSection
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class SectionNode(string id)
    {
        public string Id { get; } = id;

        public SortedDictionary<string, SectionNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<DocumentPlacement> Placements { get; } = [];
    }
}
