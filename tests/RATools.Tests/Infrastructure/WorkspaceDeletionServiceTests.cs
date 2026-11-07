using Microsoft.Extensions.Options;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;
using RATools.Tests.PackageValidation;

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
    public async Task DeleteApplicationWorkspaceAsync_RecyclesDirectoryInsideAllowedRoot()
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app-001");
        Directory.CreateDirectory(Path.Combine(workspace, "0001"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "0001", "payload.txt"), "application bytes");
        var service = CreateService(root.Path);

        await service.DeleteApplicationWorkspaceAsync(workspace);

        Assert.False(Directory.Exists(workspace));
        var retained = Assert.Single(Directory.GetDirectories(Path.Combine(root.Path, "_recycle")));
        Assert.Equal("application bytes", await File.ReadAllTextAsync(Path.Combine(retained, "0001", "payload.txt")));
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
    public async Task DeleteSequenceWorkspaceAsync_RecyclesOnlyTargetSequence()
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app-001");
        var sequenceToDelete = Path.Combine(workspace, "0001");
        var siblingSequence = Path.Combine(workspace, "0002");
        Directory.CreateDirectory(sequenceToDelete);
        Directory.CreateDirectory(siblingSequence);
        await File.WriteAllTextAsync(Path.Combine(sequenceToDelete, "payload.txt"), "sequence bytes");
        await File.WriteAllTextAsync(Path.Combine(siblingSequence, "sibling.txt"), "sibling bytes");
        var service = CreateService(root.Path);

        await service.DeleteSequenceWorkspaceAsync(workspace, "0001");

        Assert.False(Directory.Exists(sequenceToDelete));
        Assert.True(Directory.Exists(siblingSequence));
        Assert.Equal("sibling bytes", await File.ReadAllTextAsync(Path.Combine(siblingSequence, "sibling.txt")));
        var retained = Assert.Single(Directory.GetDirectories(Path.Combine(workspace, "_recycle")));
        Assert.Equal("sequence bytes", await File.ReadAllTextAsync(Path.Combine(retained, "payload.txt")));
    }

    [Fact]
    public async Task DeleteApplicationWorkspaceAsync_IsNoOpWhenDirectoryDoesNotExist()
    {
        using var root = new TemporaryDirectory();
        var missingWorkspace = Path.Combine(root.Path, "missing-app");
        var service = CreateService(root.Path);

        await service.DeleteApplicationWorkspaceAsync(missingWorkspace);

        Assert.False(Directory.Exists(missingWorkspace));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "_recycle")));
    }

    [Fact]
    public async Task RecycleFailurePreservesOriginalWorkspace()
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "payload.txt"), "retained bytes");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "_recycle"), "blocked");
        await Assert.ThrowsAsync<IOException>(() => CreateService(root.Path).DeleteApplicationWorkspaceAsync(workspace));
        Assert.Equal("retained bytes", await File.ReadAllTextAsync(Path.Combine(workspace, "payload.txt")));
        Assert.Equal("blocked", await File.ReadAllTextAsync(Path.Combine(root.Path, "_recycle")));
    }

    [Fact]
    public async Task CancellationPreservesOriginalWorkspace()
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app"); Directory.CreateDirectory(workspace);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(root.Path).DeleteApplicationWorkspaceAsync(workspace, cancelled.Token));
        Assert.True(Directory.Exists(workspace));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "_recycle")));
    }

    [Fact]
    public async Task SequenceCannotMoveAnotherApplicationInsideAllowedRoot()
    {
        using var root = new TemporaryDirectory();
        var application = Path.Combine(root.Path, "app"); var victim = Path.Combine(root.Path, "other");
        Directory.CreateDirectory(application); Directory.CreateDirectory(victim);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(root.Path).DeleteSequenceWorkspaceAsync(application, "../other"));
        Assert.True(Directory.Exists(victim));
    }

    [Fact]
    public async Task AllowedRootItselfCannotBeMovedOutsideItsBoundary()
    {
        using var root = new TemporaryDirectory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(root.Path).DeleteApplicationWorkspaceAsync(root.Path));
        Assert.True(Directory.Exists(root.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinksInWorkspaceOrRecycleAreRejectedWithoutChangingTarget(bool recycleLink)
    {
        using var root = new TemporaryDirectory();
        var workspace = Path.Combine(root.Path, "app"); var target = Path.Combine(root.Path, "outside-workspace");
        Directory.CreateDirectory(workspace); Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "sentinel.txt"), "untouched");
        var link = recycleLink ? Path.Combine(root.Path, "_recycle") : Path.Combine(workspace, "link");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            var service = CreateService(root.Path);
            if (recycleLink) await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteApplicationWorkspaceAsync(workspace));
            else await Assert.ThrowsAsync<IOException>(() => service.DeleteApplicationWorkspaceAsync(workspace));
            Assert.True(Directory.Exists(workspace));
            Assert.Equal("untouched", await File.ReadAllTextAsync(Path.Combine(target, "sentinel.txt")));
            Assert.Single(Directory.GetFileSystemEntries(target));
        }
        finally { RetainedPackageTestFiles.RetainReviewedLink(root.Path, link, target); }
    }

    [Fact]
    public async Task FilesystemRootIsRejectedBeforePolicyCanReturnDriveRelativePath()
    {
        var volume = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath("."))!;
        var policy = new RootProbePolicy(volume);
        var service = new WorkspaceDeletionService(policy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteApplicationWorkspaceAsync(volume));
        Assert.False(policy.WasCalled);
    }

    [Fact]
    public async Task RealPolicyAllowingVolumeRootStillRejectsRemovingVolumeRoot()
    {
        var volume = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath("."))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(volume).DeleteApplicationWorkspaceAsync(volume));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(volume).DeleteSequenceWorkspaceAsync(volume, "0001"));
    }

    private sealed class RootProbePolicy(string root) : RATools.Application.Abstractions.Security.IWorkspacePathPolicy
    {
        public bool WasCalled { get; private set; }
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            WasCalled = true;
            // Safe red reproduction: never permit the old implementation to
            // reinterpret a drive-relative policy result and move any directory.
            throw new IOException("Root reached the policy before the required rejection.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = RetainedPackageTestFiles.Create();
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                RetainedPackageTestFiles.RetainDirectory(Path);
            }
        }
    }
}
