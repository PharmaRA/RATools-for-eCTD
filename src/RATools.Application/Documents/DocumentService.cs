using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Application.Workspaces;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentService(
    IDocumentRepository repository,
    IFileStorage fileStorage,
    IDocumentPlacementRepository placementRepository,
    IApplicationRepository applicationRepository,
    IApplicationWorkspaceService workspaceService,
    IEctdWorkspacePathResolver workspacePathResolver,
    IWorkspacePathPolicy workspacePathPolicy) : IDocumentService
{
    public async Task<DocumentDto> UploadAsync(UploadDocumentRequest request, CancellationToken cancellationToken = default)
    {
        ValidateAllowedFileName(request.FileName);

        var storedFile = await fileStorage.SaveAsync(
            new FileUploadRequest
            {
                FileName = request.FileName,
                MediaType = EctdDocumentFileRules.GetMediaType(request.FileName),
                Content = request.Content
            },
            cancellationToken);

        var document = new SubmissionDocument(
            storedFile.FileName,
            storedFile.MediaType,
            storedFile.FileSize,
            storedFile.Sha256,
            storedFile.Md5,
            storedFile.StoragePath);

        await repository.AddAsync(document, cancellationToken);
        return document.ToDto();
    }

    public async Task<DocumentDto> UploadToSequenceAsync(Guid applicationId, string sequenceNumber, UploadSequenceDocumentRequest request, CancellationToken cancellationToken = default)
    {
        ValidateAllowedFileName(request.FileName);

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
        var folder = ResolveSequenceUploadFolder(application.EctdTemplateKey, request.CtdSection);
        var destinationDirectory = Path.Combine(sequenceDirectory, folder.RelativeFolderPath);
        workspacePathPolicy.EnsureAllowed(destinationDirectory);

        var storedFile = await fileStorage.SaveAsync(
            new FileUploadRequest
            {
                FileName = request.FileName,
                MediaType = EctdDocumentFileRules.GetMediaType(request.FileName),
                DestinationDirectoryPath = destinationDirectory,
                Content = request.Content
            },
            cancellationToken);

        var document = new SubmissionDocument(
            storedFile.FileName,
            storedFile.MediaType,
            storedFile.FileSize,
            storedFile.Sha256,
            storedFile.Md5,
            storedFile.StoragePath);

        await repository.AddAsync(document, cancellationToken);
        return document.ToDto();
    }

    private EctdWorkspacePathResolution ResolveSequenceUploadFolder(string ectdTemplateKey, string ctdSection)
    {
        try
        {
            return workspacePathResolver.Resolve(ectdTemplateKey, ctdSection);
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

    public async Task<IReadOnlyCollection<DocumentDto>> ListByApplicationAsync(
        Guid applicationId,
        string? sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var placements = string.IsNullOrWhiteSpace(sequenceNumber)
            ? await placementRepository.ListByApplicationAsync(applicationId, cancellationToken)
            : await placementRepository.ListBySequenceAsync(applicationId, sequenceNumber.Trim(), cancellationToken);
        var documentIds = placements
            .Select(x => x.DocumentId)
            .Distinct()
            .ToArray();
        var documents = await repository.ListByIdsPreferScopedAsync(documentIds, cancellationToken);
        var documentById = documents
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());

        return documentIds
            .Where(documentById.ContainsKey)
            .Select(id => documentById[id].ToDto())
            .ToArray();
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
        var sharedPathExists = allDocuments.Any(x => x.Id != id && string.Equals(x.StoragePath, document.StoragePath, StringComparison.OrdinalIgnoreCase));

        await repository.DeleteAsync(id, cancellationToken);

        if (!sharedPathExists && File.Exists(document.StoragePath))
        {
            File.Delete(document.StoragePath);
            await TryDeleteEmptyWorkspaceFoldersAsync(document.StoragePath, cancellationToken);
        }

        return true;
    }

    private static void ValidateAllowedFileName(string fileName)
    {
        if (!EctdDocumentFileRules.IsAllowedFileName(fileName))
        {
            throw new DocumentFileValidationException($"File name '{fileName}' has an invalid or unsupported extension. {EctdDocumentFileRules.BuildAllowedExtensionsMessage()}");
        }
    }

    private async Task TryDeleteEmptyWorkspaceFoldersAsync(string deletedFilePath, CancellationToken cancellationToken)
    {
        try
        {
            var sequenceRoot = await ResolveSequenceWorkspaceRootAsync(deletedFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(sequenceRoot))
            {
                return;
            }

            var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(deletedFilePath));
            EmptyWorkspaceFolderPruner.TryPruneBranches([currentDirectory], sequenceRoot);
        }
        catch
        {
            // Best-effort cleanup: deletion should succeed even if folder pruning is blocked.
        }
    }

    private async Task<string?> ResolveSequenceWorkspaceRootAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(filePath))
        {
            return null;
        }

        var normalizedFilePath = WorkspacePathGuard.Normalize(filePath);
        var applications = await applicationRepository.ListAsync(cancellationToken);

        foreach (var application in applications)
        {
            if (!Path.IsPathFullyQualified(application.WorkingDirectoryPath))
            {
                continue;
            }

            var applicationRoot = WorkspacePathGuard.Normalize(application.WorkingDirectoryPath);
            foreach (var sequence in application.Sequences)
            {
                var sequenceRoot = WorkspacePathGuard.Normalize(Path.Combine(applicationRoot, sequence.SequenceNumber));
                if (WorkspacePathGuard.IsInsideScope(normalizedFilePath, sequenceRoot))
                {
                    return sequenceRoot;
                }
            }
        }

        return null;
    }
}

public sealed class DocumentSequenceUploadTargetNotFoundException(string message) : Exception(message);

public sealed class DocumentSequenceUploadConfigurationException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class DocumentFileValidationException(string message) : Exception(message);

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
            document.Md5,
            document.StoragePath,
            document.CreatedUtc);
    }
}
