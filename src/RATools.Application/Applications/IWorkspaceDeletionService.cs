namespace RATools.Application.Applications;

public interface IWorkspaceDeletionService
{
    Task DeleteApplicationWorkspaceAsync(string applicationWorkingDirectoryPath, CancellationToken cancellationToken = default);

    Task DeleteSequenceWorkspaceAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default);
}
