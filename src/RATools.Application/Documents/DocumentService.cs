using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentService(
    IDocumentRepository repository,
    IFileStorage fileStorage,
    IDocumentPlacementRepository placementRepository,
    IApplicationRepository applicationRepository,
    IApplicationWorkspaceService workspaceService,
    IEctdWorkspacePathResolver workspacePathResolver) : IDocumentService
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

    public async Task<DocumentDto> UploadToSequenceAsync(Guid applicationId, string sequenceNumber, UploadSequenceDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken)
            ?? throw new DocumentSequenceUploadTargetNotFoundException($"Application {applicationId} was not found.");

        if (application.Sequences.All(x => x.SequenceNumber != sequenceNumber))
        {
            throw new DocumentSequenceUploadTargetNotFoundException($"Sequence {sequenceNumber} does not exist on application {applicationId}.");
        }

        if (!Path.IsPathFullyQualified(application.WorkingDirectoryPath))
        {
            throw new DocumentSequenceUploadConfigurationException($"Application {application.Id} does not have a valid working directory path configured. Legacy application data must be backfilled with a valid persisted working directory before sequence uploads can continue.");
        }

        var sequenceDirectory = await workspaceService.EnsureSequenceWorkingDirectoryAsync(application.WorkingDirectoryPath, sequenceNumber, cancellationToken);
        var folder = ResolveSequenceUploadFolder(application.Region, request.CtdSection);
        var destinationDirectory = Path.Combine(sequenceDirectory, folder.RelativeFolderPath);

        var storedFile = await fileStorage.SaveAsync(
            new FileUploadRequest
            {
                FileName = request.FileName,
                MediaType = request.MediaType,
                DestinationDirectoryPath = destinationDirectory,
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

    private EctdWorkspacePathResolution ResolveSequenceUploadFolder(string region, string ctdSection)
    {
        try
        {
            return workspacePathResolver.Resolve(region, ctdSection);
        }
        catch (InvalidOperationException exception)
        {
            throw new DocumentSequenceUploadConfigurationException(exception.Message, exception);
        }
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

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var placements = await placementRepository.ListAsync(cancellationToken);
        if (placements.Any(x => x.DocumentId == id))
        {
            throw new DocumentDeleteConflictException($"Document {id} cannot be deleted because document placements exist.");
        }

        var allDocuments = await repository.ListAsync(cancellationToken);
        var sharedPathCount = allDocuments.Count(x => x.Id != id && string.Equals(x.StoragePath, document.StoragePath, StringComparison.OrdinalIgnoreCase));

        await repository.DeleteAsync(id, cancellationToken);

        if (sharedPathCount == 0 && File.Exists(document.StoragePath))
        {
            File.Delete(document.StoragePath);
        }

        return true;
    }
}

public sealed class DocumentSequenceUploadTargetNotFoundException(string message) : Exception(message);

public sealed class DocumentSequenceUploadConfigurationException(string message, Exception? innerException = null) : Exception(message, innerException);

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
