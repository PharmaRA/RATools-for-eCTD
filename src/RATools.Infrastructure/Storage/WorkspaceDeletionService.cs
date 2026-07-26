using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;

namespace RATools.Infrastructure.Storage;

/// <summary>
/// 递归删除是全系统最高危的文件系统操作，删除目标来自数据库列而非请求参数——
/// 若历史数据被污染或误配置，缺少白名单校验就是任意目录删除。所有删除前必须
/// 经过 <see cref="IWorkspacePathPolicy.EnsureAllowed"/>。
/// </summary>
public sealed class WorkspaceDeletionService(IWorkspacePathPolicy workspacePathPolicy) : IWorkspaceDeletionService
{
    public Task DeleteApplicationWorkspaceAsync(string applicationWorkingDirectoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);

        var fullPath = workspacePathPolicy.EnsureAllowed(Path.GetFullPath(applicationWorkingDirectoryPath));
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
        var fullSequencePath = workspacePathPolicy.EnsureAllowed(
            Path.GetFullPath(Path.Combine(fullApplicationPath, sequenceNumber)));
        if (Directory.Exists(fullSequencePath))
        {
            Directory.Delete(fullSequencePath, true);
        }

        return Task.CompletedTask;
    }
}
