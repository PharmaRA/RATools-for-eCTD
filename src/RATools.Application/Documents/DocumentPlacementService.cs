using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentPlacementService(
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IApplicationRepository applicationRepository) : IDocumentPlacementService
{
    public async Task<DocumentPlacementDto> CreateAsync(CreateDocumentPlacementRequest request, CancellationToken cancellationToken = default)
    {
        var document = await documentRepository.GetAsync(request.DocumentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException($"Document {request.DocumentId} was not found.");
        }

        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            throw new InvalidOperationException($"Application {request.ApplicationId} was not found.");
        }

        if (application.Sequences.All(x => x.SequenceNumber != request.SequenceNumber))
        {
            throw new InvalidOperationException($"Sequence {request.SequenceNumber} does not exist on application {request.ApplicationId}.");
        }

        if (!Enum.TryParse<DocumentPlacementOperation>(request.Operation, ignoreCase: true, out var operation))
        {
            throw new InvalidOperationException($"Unsupported placement operation '{request.Operation}'.");
        }

        var placement = new DocumentPlacement(
            request.DocumentId,
            request.ApplicationId,
            request.SequenceNumber,
            request.CtdSection,
            operation,
            request.Title);

        await placementRepository.AddAsync(placement, cancellationToken);
        return placement.ToDto();
    }

    public async Task<IReadOnlyCollection<DocumentPlacementDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await placementRepository.ListAsync(cancellationToken);
        return items.Select(x => x.ToDto()).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentPlacementDto>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var items = await placementRepository.ListByApplicationAsync(applicationId, cancellationToken);
        return items.Select(x => x.ToDto()).ToArray();
    }
}

internal static class DocumentPlacementMapping
{
    public static DocumentPlacementDto ToDto(this DocumentPlacement placement)
    {
        return new DocumentPlacementDto(
            placement.Id,
            placement.DocumentId,
            placement.ApplicationId,
            placement.SequenceNumber,
            placement.CtdSection,
            placement.Operation.ToString(),
            placement.Title,
            placement.CreatedUtc);
    }
}
