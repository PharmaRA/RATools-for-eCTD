using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentPlacementService(
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IApplicationRepository applicationRepository,
    IPublishJobRepository publishJobRepository,
    IEctdWorkspacePathResolver workspacePathResolver) : IDocumentPlacementService
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

    public async Task<DocumentPlacementDto?> UpdateSectionAsync(Guid id, UpdateDocumentPlacementSectionRequest request, CancellationToken cancellationToken = default)
    {
        var placement = await placementRepository.GetAsync(id, cancellationToken);
        if (placement is null)
        {
            return null;
        }

        var document = await documentRepository.GetAsync(placement.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {placement.DocumentId} was not found.");
        var application = await applicationRepository.GetAsync(placement.ApplicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Application {placement.ApplicationId} was not found.");

        if (!Path.IsPathFullyQualified(application.WorkingDirectoryPath))
        {
            throw new InvalidOperationException($"Application {application.Id} does not have a valid working directory path configured.");
        }

        if (application.Sequences.All(x => x.SequenceNumber != placement.SequenceNumber))
        {
            throw new InvalidOperationException($"Sequence {placement.SequenceNumber} does not exist on application {placement.ApplicationId}.");
        }

        var siblingPlacements = await placementRepository.ListAsync(cancellationToken);
        if (siblingPlacements.Any(x => x.DocumentId == placement.DocumentId && x.Id != placement.Id))
        {
            throw new InvalidOperationException($"Document placement {placement.Id} cannot be reassigned because another placement still references document {placement.DocumentId}.");
        }

        var originalStoragePath = ValidateDocumentStoragePath(document, application, placement.SequenceNumber);

        var oldFolder = workspacePathResolver.Resolve(application.Region, placement.CtdSection);
        var newFolder = workspacePathResolver.Resolve(application.Region, request.CtdSection);
        var originalSection = placement.CtdSection;
        string? movedStoragePath = null;

        if (!string.Equals(oldFolder.RelativeFolderPath, newFolder.RelativeFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            var targetDirectory = Path.Combine(application.WorkingDirectoryPath, placement.SequenceNumber, newFolder.RelativeFolderPath);
            var targetPath = Path.Combine(targetDirectory, Path.GetFileName(originalStoragePath));

            if (!string.Equals(originalStoragePath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                movedStoragePath = await fileStorage.MoveAsync(originalStoragePath, targetDirectory, cancellationToken);
                document.Relocate(movedStoragePath);
            }
        }

        try
        {
            if (movedStoragePath is not null && !await documentRepository.UpdateAsync(document, cancellationToken))
            {
                throw new InvalidOperationException($"Document {document.Id} could not be updated after moving the stored file.");
            }

            placement.ReassignSection(request.CtdSection);
            if (await placementRepository.UpdateAsync(placement, cancellationToken))
            {
                return placement.ToDto();
            }

            throw new InvalidOperationException($"Document placement {placement.Id} could not be updated.");
        }
        catch when (movedStoragePath is not null)
        {
            placement.ReassignSection(originalSection);
            await RollbackMoveAsync(document, originalStoragePath, movedStoragePath, cancellationToken);
            throw;
        }
    }

    private static string ValidateDocumentStoragePath(SubmissionDocument document, Domain.Applications.SubmissionApplication application, string sequenceNumber)
    {
        if (!Path.IsPathFullyQualified(document.StoragePath))
        {
            throw new InvalidOperationException($"Document {document.Id} does not have a valid fully qualified storage path configured.");
        }

        var fullStoragePath = Path.GetFullPath(document.StoragePath);
        var workspaceRoot = Path.GetFullPath(application.WorkingDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspacePrefix = workspaceRoot + Path.DirectorySeparatorChar;

        if (!fullStoragePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Document {document.Id} storage path is outside the application workspace.");
        }

        var expectedSequenceRoot = Path.Combine(workspaceRoot, sequenceNumber)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullStoragePath.StartsWith(expectedSequenceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Document {document.Id} storage path must remain under sequence {sequenceNumber}.");
        }

        return fullStoragePath;
    }

    private async Task RollbackMoveAsync(SubmissionDocument document, string originalStoragePath, string movedStoragePath, CancellationToken cancellationToken)
    {
        var restoredPath = await fileStorage.MoveAsync(movedStoragePath, Path.GetDirectoryName(originalStoragePath)!, cancellationToken);
        document.Relocate(restoredPath);
        if (!await documentRepository.UpdateAsync(document, cancellationToken))
        {
            throw new InvalidOperationException($"Document {document.Id} could not be restored after a failed section reassignment.");
        }
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

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var placement = await placementRepository.GetAsync(id, cancellationToken);
        if (placement is null)
        {
            return false;
        }

        var publishJobs = await publishJobRepository.QueryHistoryAsync(
            new PublishJobHistoryQuery(placement.ApplicationId, null, null, null, null, 1, 1),
            cancellationToken);

        if (publishJobs.TotalCount > 0)
        {
            throw new DocumentPlacementDeleteConflictException($"Document placement {id} cannot be deleted because publish jobs exist for application {placement.ApplicationId}.");
        }

        await placementRepository.DeleteAsync(id, cancellationToken);
        return true;
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
