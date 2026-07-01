using RATools.Application.Abstractions.Security;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Infrastructure;

public sealed class LocalPublishArtifactStoreTests
{
    [Fact]
    public async Task ExistsAsync_UsesWorkspacePolicyBeforeCheckingPath()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "artifact.json");
        await File.WriteAllTextAsync(filePath, "{}");
        var policy = new RecordingWorkspacePathPolicy();
        var store = new LocalPublishArtifactStore(policy);

        var exists = await store.ExistsAsync(filePath);

        Assert.True(exists);
        Assert.Contains(filePath, policy.CheckedPaths);
        DeleteIfExists(root);
    }

    [Fact]
    public async Task ReadWriteAndSizeAsync_UseWorkspacePolicyForEachIoEntry()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "nested", "report.json");
        var policy = new RecordingWorkspacePathPolicy();
        var store = new LocalPublishArtifactStore(policy);

        await store.WriteAllTextAsync(filePath, "report");
        var content = await store.ReadAllTextAsync(filePath);
        var size = await store.GetSizeAsync(filePath);

        Assert.Equal("report", content);
        Assert.Equal(6, size);
        Assert.Equal(3, policy.CheckedPaths.Count(path => path == filePath));
        DeleteIfExists(root);
    }

    [Fact]
    public async Task GetDirectoryStatsAsync_ReturnsRecursiveFileCountAndTotalSize()
    {
        var root = CreateTempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "one");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(root, "nested", "two.txt"), "two!");
        var policy = new RecordingWorkspacePathPolicy();
        var store = new LocalPublishArtifactStore(policy);

        var stats = await store.GetDirectoryStatsAsync(root);

        Assert.Equal(2, stats.FileCount);
        Assert.Equal(7, stats.TotalSizeBytes);
        Assert.Contains(root, policy.CheckedPaths);
        DeleteIfExists(root);
    }

    [Fact]
    public async Task IoMethods_PropagateWorkspacePolicyRejections()
    {
        var policy = new RejectingWorkspacePathPolicy();
        var store = new LocalPublishArtifactStore(policy);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExistsAsync("blocked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetSizeAsync("blocked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAllTextAsync("blocked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAllTextAsync("blocked", "content"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetDirectoryStatsAsync("blocked"));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-artifact-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public List<string> CheckedPaths { get; } = [];

        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path)
        {
            CheckedPaths.Add(path);
            return Path.GetFullPath(path);
        }
    }

    private sealed class RejectingWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path)
            => throw new InvalidOperationException($"Path '{path}' is blocked.");
    }
}
