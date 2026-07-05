using RATools.Application.Abstractions.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;

namespace RATools.Application.Applications;

public sealed class ApplicationDeletionCoordinator(
    IApplicationRepository applicationRepository,
    IDocumentRepository documentRepository,
    IDocumentPlacementRepository placementRepository,
    IPublishJobRepository publishJobRepository,
    IWorkspaceDeletionService workspaceDeletionService,
    IApplicationDeletionTransaction transaction) : IApplicationDeletionCoordinator
{
    public async Task DeleteApplicationAsync(
        SubmissionApplication application,
        ApplicationDeleteMode deleteMode,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = application.WorkingDirectoryPath;
        var purgePaths = new List<string>();

        await transaction.ExecuteAsync(async ct =>
        {
            var allPlacements = await placementRepository.ListAsync(ct);
            var allDocuments = await documentRepository.ListAsync(ct);
            var allPublishJobs = await publishJobRepository.ListAsync(ct);
            var scope = ApplicationDeletionScope.ForApplication(application.Id, allPlacements, allPublishJobs);

            if (deleteMode == ApplicationDeleteMode.PurgeWorkspace)
            {
                EnsureApplicationPurgeIsSafe(workspacePath, allDocuments, scope.SurvivingDocumentIds);
                EnsurePublishJobPurgeIsSafeForApplication(application.Id, workspacePath, allPublishJobs);
                purgePaths.AddRange(scope.CollectPurgePaths(allDocuments));
            }

            foreach (var placement in scope.InScopePlacements)
            {
                await placementRepository.DeleteAsync(placement.Id, ct);
            }

            await publishJobRepository.DeleteByApplicationAsync(application.Id, ct);

            foreach (var documentId in scope.OrphanedDocumentIds)
            {
                await documentRepository.DeleteAsync(documentId, ct);
            }

            await applicationRepository.DeleteAsync(application.Id, ct);
        }, cancellationToken);

        if (deleteMode == ApplicationDeleteMode.PurgeWorkspace)
        {
            PurgeScopedPaths(purgePaths, workspacePath);
            await TryPurgeWorkspaceAsync(
                () => workspaceDeletionService.DeleteApplicationWorkspaceAsync(workspacePath, cancellationToken));
        }
    }

    public async Task<bool> DeleteSequenceAsync(
        SubmissionApplication application,
        string sequenceNumber,
        ApplicationDeleteMode deleteMode,
        CancellationToken cancellationToken = default)
    {
        if (application.Sequences.All(x => !string.Equals(x.SequenceNumber, sequenceNumber, StringComparison.Ordinal)))
        {
            return false;
        }

        var workspacePath = application.WorkingDirectoryPath;
        var purgePaths = new List<string>();

        var deleted = await transaction.ExecuteAsync(async ct =>
        {
            var allPlacements = await placementRepository.ListAsync(ct);
            var allDocuments = await documentRepository.ListAsync(ct);
            var allPublishJobs = await publishJobRepository.ListAsync(ct);
            var scope = ApplicationDeletionScope.ForSequence(application.Id, sequenceNumber, allPlacements, allPublishJobs);

            if (deleteMode == ApplicationDeleteMode.PurgeWorkspace)
            {
                var sequenceWorkspacePath = EnsureSequencePurgeIsSafe(workspacePath, sequenceNumber, allDocuments, scope.SurvivingDocumentIds);
                EnsurePublishJobPurgeIsSafeForSequence(application.Id, sequenceNumber, sequenceWorkspacePath, allPublishJobs);
                purgePaths.AddRange(scope.CollectPurgePaths(allDocuments));
            }

            foreach (var placement in scope.InScopePlacements)
            {
                await placementRepository.DeleteAsync(placement.Id, ct);
            }

            await publishJobRepository.DeleteBySequenceAsync(application.Id, sequenceNumber, ct);

            foreach (var documentId in scope.OrphanedDocumentIds)
            {
                await documentRepository.DeleteAsync(documentId, ct);
            }

            var removed = application.RemoveSequence(sequenceNumber);
            if (!removed)
            {
                return false;
            }

            await applicationRepository.UpdateAsync(application, ct);
            return true;
        }, cancellationToken);

        if (!deleted)
        {
            return false;
        }

        if (deleteMode == ApplicationDeleteMode.PurgeWorkspace)
        {
            var sequenceWorkspacePath = ResolveSequenceWorkspacePath(workspacePath, sequenceNumber);
            PurgeScopedPaths(purgePaths, sequenceWorkspacePath);
            await TryPurgeWorkspaceAsync(
                () => workspaceDeletionService.DeleteSequenceWorkspaceAsync(workspacePath, sequenceNumber, cancellationToken));
        }

        return true;
    }

    private static void EnsurePublishJobPurgeIsSafeForApplication(
        Guid applicationId,
        string applicationWorkspacePath,
        IReadOnlyCollection<Domain.Publishing.PublishJob> allPublishJobs)
    {
        var conflict = allPublishJobs
            .Where(x => x.ApplicationId != applicationId)
            .FirstOrDefault(x =>
                WorkspacePathGuard.IsInsideScope(x.OutputPath, applicationWorkspacePath)
                || WorkspacePathGuard.IsInsideScope(x.PackagePath, applicationWorkspacePath));

        if (conflict is not null)
        {
            throw new ApplicationDeleteConflictException(
                $"Application {applicationId} cannot purge workspace because publish job {conflict.Id} from another scope still references files under the application workspace.");
        }
    }

    private static void EnsurePublishJobPurgeIsSafeForSequence(
        Guid applicationId,
        string sequenceNumber,
        string sequenceWorkspacePath,
        IReadOnlyCollection<Domain.Publishing.PublishJob> allPublishJobs)
    {
        var conflict = allPublishJobs
            .Where(x => x.ApplicationId != applicationId || !string.Equals(x.SequenceNumber, sequenceNumber, StringComparison.Ordinal))
            .FirstOrDefault(x =>
                WorkspacePathGuard.IsInsideScope(x.OutputPath, sequenceWorkspacePath)
                || WorkspacePathGuard.IsInsideScope(x.PackagePath, sequenceWorkspacePath));

        if (conflict is not null)
        {
            throw new SequenceDeleteConflictException(
                $"Sequence {sequenceNumber} cannot purge workspace because publish job {conflict.Id} outside the delete scope still references files under the sequence workspace.");
        }
    }

    private static void EnsureApplicationPurgeIsSafe(
        string applicationWorkspacePath,
        IReadOnlyCollection<Domain.Documents.SubmissionDocument> allDocuments,
        IReadOnlySet<Guid> survivingDocumentIds)
    {
        if (!Path.IsPathFullyQualified(applicationWorkspacePath))
        {
            throw new ApplicationDeleteConflictException(
                "Application workspace path is not fully qualified. Purge workspace is blocked for safety.");
        }

        var conflict = allDocuments
            .Where(x => survivingDocumentIds.Contains(x.Id))
            .FirstOrDefault(x => WorkspacePathGuard.IsInsideScope(x.StoragePath, applicationWorkspacePath));

        if (conflict is not null)
        {
            throw new ApplicationDeleteConflictException(
                $"Application purge is blocked because document {conflict.Id} is still referenced outside delete scope but stored under the application workspace.");
        }
    }

    private static string EnsureSequencePurgeIsSafe(
        string applicationWorkspacePath,
        string sequenceNumber,
        IReadOnlyCollection<Domain.Documents.SubmissionDocument> allDocuments,
        IReadOnlySet<Guid> survivingDocumentIds)
    {
        var sequenceWorkspacePath = ResolveSequenceWorkspacePath(applicationWorkspacePath, sequenceNumber);

        var conflict = allDocuments
            .Where(x => survivingDocumentIds.Contains(x.Id))
            .FirstOrDefault(x => WorkspacePathGuard.IsInsideScope(x.StoragePath, sequenceWorkspacePath));

        if (conflict is not null)
        {
            throw new SequenceDeleteConflictException(
                $"Sequence {sequenceNumber} cannot purge workspace because document {conflict.Id} is still referenced outside delete scope.");
        }

        return sequenceWorkspacePath;
    }

    private static string ResolveSequenceWorkspacePath(string applicationWorkspacePath, string sequenceNumber)
    {
        if (!Path.IsPathFullyQualified(applicationWorkspacePath))
        {
            throw new SequenceDeleteConflictException(
                "Application workspace path is not fully qualified. Purge workspace is blocked for safety.");
        }

        var root = WorkspacePathGuard.Normalize(applicationWorkspacePath);
        var sequenceWorkspacePath = WorkspacePathGuard.Normalize(Path.Combine(root, sequenceNumber));

        if (!WorkspacePathGuard.IsInsideScope(sequenceWorkspacePath, root))
        {
            throw new SequenceDeleteConflictException(
                $"Sequence '{sequenceNumber}' escapes application workspace and cannot be purged safely.");
        }

        if (string.Equals(sequenceWorkspacePath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new SequenceDeleteConflictException(
                $"Sequence '{sequenceNumber}' escapes application workspace and cannot be purged safely.");
        }

        return sequenceWorkspacePath;
    }

    private static void PurgeScopedPaths(IReadOnlyCollection<string> candidatePaths, string scopeRoot)
    {
        foreach (var candidatePath in candidatePaths
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(x => x.Length))
        {
            if (!WorkspacePathGuard.IsInsideScope(candidatePath, scopeRoot))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(candidatePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        }
    }

    private static async Task TryPurgeWorkspaceAsync(Func<Task> purgeAction)
    {
        try
        {
            await purgeAction();
        }
        catch (Exception exception)
        {
            throw new WorkspacePurgeFailedException(
                "Workspace purge failed after database delete succeeded.",
                exception);
        }
    }
}
