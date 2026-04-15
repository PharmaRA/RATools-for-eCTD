using RATools.Application.Applications;

namespace RATools.Infrastructure.Storage;

public sealed class WorkspaceDeletionService : IWorkspaceDeletionService
{
    public Task DeleteApplicationWorkspaceAsync(string applicationWorkingDirectoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);

        var fullPath = Path.GetFullPath(applicationWorkingDirectoryPath);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteSequenceWorkspaceAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var fullApplicationPath = Path.GetFullPath(applicationWorkingDirectoryPath);
        var fullSequencePath = Path.GetFullPath(Path.Combine(fullApplicationPath, sequenceNumber));
        if (Directory.Exists(fullSequencePath))
        {
            Directory.Delete(fullSequencePath, true);
        }

        return Task.CompletedTask;
    }
}
