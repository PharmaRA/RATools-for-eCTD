using Microsoft.Extensions.Options;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Infrastructure;

public sealed class WorkspaceDeletionServiceTests
{
    private static WorkspaceDeletionService CreateService(params string[] allowedRoots)
    {
        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = allowedRoots,
        }));

        return new WorkspaceDeletionService(policy);
    }

    [Fact]
    public async Task DeleteApplicationWorkspaceAsync_DeletesDirectoryInsideAllowedRoot()
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app-001");
        Directory.CreateDirectory(Path.Combine(workspace, "0001"));
        var service = CreateService(root.Path);

        await service.DeleteApplicationWorkspaceAsync(workspace);

        Assert.False(Directory.Exists(workspace));
    }

    [Fact]
    public async Task DeleteApplicationWorkspaceAsync_RejectsPathOutsideAllowedRoots()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var outsideWorkspace = Path.Combine(outsideRoot.Path, "app-001");
        Directory.CreateDirectory(outsideWorkspace);
        var service = CreateService(allowedRoot.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteApplicationWorkspaceAsync(outsideWorkspace));
        Assert.True(Directory.Exists(outsideWorkspace));
    }

    [Fact]
    public async Task DeleteSequenceWorkspaceAsync_RejectsSequenceSegmentEscapingAllowedRoot()
    {
        // sequenceNumber 来自请求，含 ".." 时 Path.Combine 会逃逸出允许根——
        // 必须被策略拦下，且逃逸目标目录不能被删除。
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var applicationWorkspace = Path.Combine(allowedRoot.Path, "app-001");
        Directory.CreateDirectory(applicationWorkspace);
        var escapeTarget = Path.Combine(outsideRoot.Path, "victim");
        Directory.CreateDirectory(escapeTarget);
        var escapingSequence = Path.Combine("..", "..", Path.GetFileName(outsideRoot.Path), "victim");
        var service = CreateService(allowedRoot.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteSequenceWorkspaceAsync(applicationWorkspace, escapingSequence));
        Assert.True(Directory.Exists(escapeTarget));
    }

    [Fact]
    public async Task DeleteSequenceWorkspaceAsync_DeletesOnlyTargetSequence()
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app-001");
        var sequenceToDelete = Path.Combine(workspace, "0001");
        var siblingSequence = Path.Combine(workspace, "0002");
        Directory.CreateDirectory(sequenceToDelete);
        Directory.CreateDirectory(siblingSequence);
        var service = CreateService(root.Path);

        await service.DeleteSequenceWorkspaceAsync(workspace, "0001");

        Assert.False(Directory.Exists(sequenceToDelete));
        Assert.True(Directory.Exists(siblingSequence));
    }

    [Fact]
    public async Task DeleteApplicationWorkspaceAsync_IsNoOpWhenDirectoryDoesNotExist()
    {
        using var root = new TemporaryDirectory();
        var missingWorkspace = Path.Combine(root.Path, "missing-app");
        var service = CreateService(root.Path);

        await service.DeleteApplicationWorkspaceAsync(missingWorkspace);

        Assert.False(Directory.Exists(missingWorkspace));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-deletion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
