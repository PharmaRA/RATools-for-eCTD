using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Application.Workspaces;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentPlacementService(
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IApplicationRepository applicationRepository,
    IPublishJobRepository publishJobRepository,
    IEctdWorkspacePathResolver workspacePathResolver,
    IDocumentStorageBoundary documentStorageBoundary,
    IPersistenceTransaction persistenceTransaction) : IDocumentPlacementService
{
    private static readonly TimeSpan MetadataCleanupTimeout = TimeSpan.FromSeconds(30);

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

        documentStorageBoundary.EnsureDocumentOwnedBySequence(document, application, request.SequenceNumber);

        if (!Enum.TryParse<DocumentPlacementOperation>(request.Operation, ignoreCase: true, out var operation)
            || !Enum.IsDefined(operation))
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

        var originalStoragePath = documentStorageBoundary.EnsureDocumentOwnedBySequence(document, application, placement.SequenceNumber);

        var oldFolder = workspacePathResolver.Resolve(application.EctdTemplateKey, placement.CtdSection);
        var newFolder = workspacePathResolver.Resolve(application.EctdTemplateKey, request.CtdSection);
        var originalSection = placement.CtdSection;
        string? movedStoragePath = null;

        if (!string.Equals(oldFolder.RelativeFolderPath, newFolder.RelativeFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            var targetDirectory = Path.Combine(application.WorkingDirectoryPath, placement.SequenceNumber, newFolder.RelativeFolderPath);
            documentStorageBoundary.EnsurePathOwnedBySequence(targetDirectory, application, placement.SequenceNumber);
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
                if (movedStoragePath is not null)
                {
                    TryDeleteEmptySourceFolders(application.WorkingDirectoryPath, placement.SequenceNumber, originalStoragePath, oldFolder.RelativeFolderPath);
                }

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

    private async Task RollbackMoveAsync(SubmissionDocument document, string originalStoragePath, string movedStoragePath, CancellationToken cancellationToken)
    {
        var restoredPath = await fileStorage.MoveAsync(movedStoragePath, Path.GetDirectoryName(originalStoragePath)!, cancellationToken);
        document.Relocate(restoredPath);
        if (!await documentRepository.UpdateAsync(document, cancellationToken))
        {
            throw new InvalidOperationException($"Document {document.Id} could not be restored after a failed section reassignment.");
        }
    }

    public async Task<DocumentPlacementDto?> UpdateMetadataAsync(Guid id, UpdateDocumentPlacementMetadataRequest request, CancellationToken cancellationToken = default)
    {
        var placement = await placementRepository.GetAsync(id, cancellationToken);
        if (placement is null)
        {
            return null;
        }

        var document = await documentRepository.GetAsync(placement.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {placement.DocumentId} was not found.");

        if (!Enum.TryParse<DocumentPlacementOperation>(request.Operation, ignoreCase: true, out var operation)
            || !Enum.IsDefined(operation))
        {
            throw new InvalidOperationException($"Unsupported placement operation '{request.Operation}'.");
        }

        var lifecycleTargetPlacementId = operation == DocumentPlacementOperation.New
            ? null
            : request.LifecycleTargetPlacementId;

        var normalizedPrefix = NormalizeAndValidatePrefix(request.FileNamePrefix);
        var extension = Path.GetExtension(document.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException($"Document {document.Id} file name does not contain a valid extension.");
        }

        var normalizedExtension = extension.ToLowerInvariant();
        var targetFileName = normalizedPrefix + normalizedExtension;

        var application = await applicationRepository.GetAsync(placement.ApplicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Application {placement.ApplicationId} was not found.");
        var sourcePath = ResolveSourcePathForRename(document, application, placement.SequenceNumber);
        if (!string.Equals(sourcePath, Path.GetFullPath(document.StoragePath), StringComparison.OrdinalIgnoreCase))
        {
            document.Relocate(sourcePath);
        }

        var originalState = new DocumentMetadataState(
            placement.Title,
            placement.Operation,
            placement.LifecycleTargetPlacementId,
            document.FileName,
            document.MediaType,
            sourcePath);

        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException($"Document {document.Id} storage path does not have a parent directory.");
        var targetPath = Path.Combine(sourceDirectory, targetFileName);
        var movedStoragePath = sourcePath;

        if (!string.Equals(sourcePath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(targetPath))
            {
                throw new InvalidOperationException($"A file named '{targetFileName}' already exists in this workspace folder.");
            }

            try
            {
                movedStoragePath = await fileStorage.RenameAsync(
                    sourcePath,
                    Path.GetFullPath(targetPath),
                    cancellationToken);
            }
            catch (FileNotFoundException exception)
            {
                throw new InvalidOperationException($"Unable to rename workspace file because source workspace file '{sourcePath}' was not found.", exception);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException($"Unable to rename workspace file '{sourcePath}' to '{targetPath}'.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException($"Unable to rename workspace file '{sourcePath}' due to insufficient permissions.", exception);
            }

            document.Relocate(movedStoragePath);
        }

        var mediaType = EctdDocumentFileRules.GetMediaType(targetFileName);
        placement.ReviseTitle(request.Title);
        placement.ReviseOperation(operation);
        placement.ReviseLifecycleTarget(lifecycleTargetPlacementId);
        document.ReviseFileMetadata(targetFileName, mediaType);

        var updatedState = new DocumentMetadataState(
            placement.Title,
            placement.Operation,
            placement.LifecycleTargetPlacementId,
            document.FileName,
            document.MediaType,
            movedStoragePath);

        try
        {
            await persistenceTransaction.ExecuteAsync(async transactionToken =>
            {
                if (!await documentRepository.UpdateAsync(document, transactionToken))
                {
                    throw new InvalidOperationException($"Document {document.Id} could not be updated.");
                }

                if (!await placementRepository.UpdateAsync(placement, transactionToken))
                {
                    throw new InvalidOperationException($"Document placement {placement.Id} could not be updated.");
                }
            }, cancellationToken);

            return placement.ToDto();
        }
        catch (Exception exception)
        {
            await CompensateMetadataUpdateAsync(
                document,
                placement,
                originalState,
                updatedState,
                exception);

            throw;
        }
    }

    private async Task CompensateMetadataUpdateAsync(
        SubmissionDocument document,
        DocumentPlacement placement,
        DocumentMetadataState originalState,
        DocumentMetadataState updatedState,
        Exception originalException)
    {
        using var cleanupCts = new CancellationTokenSource(MetadataCleanupTimeout);
        var compensationFailures = new List<Exception>();
        var restoredOriginalFile = string.Equals(
            originalState.StoragePath,
            updatedState.StoragePath,
            StringComparison.OrdinalIgnoreCase);

        if (!restoredOriginalFile)
        {
            try
            {
                await fileStorage.RenameAsync(
                    updatedState.StoragePath,
                    originalState.StoragePath,
                    cleanupCts.Token);
                restoredOriginalFile = true;
            }
            catch (Exception exception)
            {
                compensationFailures.Add(exception);
            }
        }

        // If the file cannot be moved back, preserving the new database state is the
        // only recoverable way to keep persisted metadata aligned with the physical file.
        ApplyMetadataState(
            document,
            placement,
            restoredOriginalFile ? originalState : updatedState);

        try
        {
            await persistenceTransaction.ExecuteAsync(async transactionToken =>
            {
                if (!await documentRepository.UpdateAsync(document, transactionToken))
                {
                    throw new InvalidOperationException($"Document {document.Id} could not be restored.");
                }

                if (!await placementRepository.UpdateAsync(placement, transactionToken))
                {
                    throw new InvalidOperationException($"Document placement {placement.Id} could not be restored.");
                }
            }, cleanupCts.Token);
        }
        catch (Exception exception)
        {
            compensationFailures.Add(exception);
        }

        if (compensationFailures.Count > 0)
        {
            var message = restoredOriginalFile
                ? $"Metadata update for document {document.Id} failed and automatic compensation was incomplete."
                : $"Metadata update for document {document.Id} failed; the file could not be restored, so updated metadata was preserved to keep storage consistent.";

            throw new InvalidOperationException(
                message,
                new AggregateException([originalException, .. compensationFailures]));
        }
    }

    private static void ApplyMetadataState(
        SubmissionDocument document,
        DocumentPlacement placement,
        DocumentMetadataState state)
    {
        placement.ReviseTitle(state.Title);
        placement.ReviseOperation(state.Operation);
        placement.ReviseLifecycleTarget(state.LifecycleTargetPlacementId);
        document.ReviseFileMetadata(state.FileName, state.MediaType);
        document.Relocate(state.StoragePath);
    }

    private string ResolveSourcePathForRename(SubmissionDocument document, Domain.Applications.SubmissionApplication application, string sequenceNumber)
    {
        var validatedPath = documentStorageBoundary.EnsureDocumentOwnedBySequence(document, application, sequenceNumber);
        if (File.Exists(validatedPath))
        {
            return validatedPath;
        }

        var directory = Path.GetDirectoryName(validatedPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return validatedPath;
        }

        var extension = Path.GetExtension(document.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(validatedPath);
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            return validatedPath;
        }

        var candidates = Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 1)
        {
            return Path.GetFullPath(candidates[0]);
        }

        if (candidates.Length > 1)
        {
            throw new InvalidOperationException($"Unable to resolve source workspace file for rename because multiple '{extension}' files exist in '{directory}'.");
        }

        return validatedPath;
    }

    private static string NormalizeAndValidatePrefix(string fileNamePrefix)
    {
        var normalized = fileNamePrefix?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("File name prefix cannot be empty.");
        }

        if (normalized.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidOperationException("File name prefix cannot contain path separators.");
        }

        return normalized;
    }

    private static void TryDeleteEmptySourceFolders(string applicationWorkingDirectoryPath, string sequenceNumber, string sourceFilePath, string oldRelativeFolderPath)
    {
        try
        {
            var sequenceRoot = WorkspacePathGuard.Normalize(Path.Combine(applicationWorkingDirectoryPath, sequenceNumber));
            var sourceDirectory = Path.GetDirectoryName(WorkspacePathGuard.Normalize(sourceFilePath));
            var canonicalSourceDirectory = string.IsNullOrWhiteSpace(oldRelativeFolderPath)
                ? null
                : Path.Combine(sequenceRoot, oldRelativeFolderPath);

            EmptyWorkspaceFolderPruner.TryPruneBranches([sourceDirectory, canonicalSourceDirectory], sequenceRoot);
        }
        catch
        {
            // Best-effort cleanup: section reassignment succeeds even if empty-folder pruning is blocked.
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

    private sealed record DocumentMetadataState(
        string? Title,
        DocumentPlacementOperation Operation,
        Guid? LifecycleTargetPlacementId,
        string FileName,
        string MediaType,
        string StoragePath);
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
            placement.LifecycleTargetPlacementId,
            placement.CreatedUtc);
    }
}
