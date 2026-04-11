using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;

namespace RATools.Application.Documents;

public interface IDocumentService
{
    Task<DocumentDto> CreateAsync(CreateDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDto> UploadAsync(UploadDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDto> UploadToSequenceAsync(Guid applicationId, string sequenceNumber, UploadDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DocumentDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
