using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentService(IDocumentRepository repository, IFileStorage fileStorage) : IDocumentService
{
    public async Task<DocumentDto> CreateAsync(CreateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var document = new SubmissionDocument(
            request.FileName,
            request.MediaType,
            request.FileSize,
            request.Sha256,
            request.StoragePath);

        await repository.AddAsync(document, cancellationToken);
        return document.ToDto();
    }

    public async Task<DocumentDto> UploadAsync(UploadDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var storedFile = await fileStorage.SaveAsync(
            new FileUploadRequest
            {
                FileName = request.FileName,
                MediaType = request.MediaType,
                Content = request.Content
            },
            cancellationToken);

        var document = new SubmissionDocument(
            storedFile.FileName,
            storedFile.MediaType,
            storedFile.FileSize,
            storedFile.Sha256,
            storedFile.StoragePath);

        await repository.AddAsync(document, cancellationToken);
        return document.ToDto();
    }

    public async Task<DocumentDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetAsync(id, cancellationToken);
        return document?.ToDto();
    }

    public async Task<IReadOnlyCollection<DocumentDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var documents = await repository.ListAsync(cancellationToken);
        return documents.Select(x => x.ToDto()).ToArray();
    }
}

internal static class DocumentMapping
{
    public static DocumentDto ToDto(this SubmissionDocument document)
    {
        return new DocumentDto(
            document.Id,
            document.FileName,
            document.MediaType,
            document.FileSize,
            document.Sha256,
            document.StoragePath,
            document.CreatedUtc);
    }
}
