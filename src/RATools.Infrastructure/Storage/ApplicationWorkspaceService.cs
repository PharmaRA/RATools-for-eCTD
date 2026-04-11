using RATools.Application.Abstractions.Storage;

namespace RATools.Infrastructure.Storage;

public sealed class ApplicationWorkspaceService : IApplicationWorkspaceService
{
    public Task<string> EnsureApplicationWorkingDirectoryAsync(string parentPath, string applicationNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNumber);

        var path = Path.Combine(parentPath.Trim(), applicationNumber.Trim());
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }

    public Task<string> EnsureSequenceWorkingDirectoryAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var path = Path.Combine(applicationWorkingDirectoryPath.Trim(), sequenceNumber.Trim());
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }
}
