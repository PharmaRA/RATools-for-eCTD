using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;

namespace RATools.Infrastructure.Storage;

/// <summary>
/// 工作目录创建同样必须过白名单：applicationNumber/sequenceNumber 来自请求，
/// 若含 ".." 等相对段，Path.Combine 后可能逃逸到允许根之外再 CreateDirectory。
/// </summary>
public sealed class ApplicationWorkspaceService(IWorkspacePathPolicy workspacePathPolicy) : IApplicationWorkspaceService
{
    public Task<string> EnsureApplicationWorkingDirectoryAsync(string parentPath, string applicationNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNumber);

        var path = workspacePathPolicy.EnsureAllowed(Path.GetFullPath(Path.Combine(parentPath, applicationNumber)));
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }

    public Task<string> EnsureSequenceWorkingDirectoryAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var path = workspacePathPolicy.EnsureAllowed(Path.GetFullPath(Path.Combine(applicationWorkingDirectoryPath, sequenceNumber)));
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }
}
