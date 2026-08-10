using Microsoft.Extensions.Options;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Infrastructure;

[Trait("Category", "PathSecurity")]
public sealed class ApplicationWorkspaceServiceTests
{
    private static ApplicationWorkspaceService CreateService(params string[] allowedRoots)
    {
        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = allowedRoots,
        }));

        return new ApplicationWorkspaceService(policy);
    }

    [Fact]
    public async Task EnsureApplicationWorkingDirectoryAsync_RejectsNonCanonicalPathSegment()
    {
        using var parent = new TemporaryDirectory();
        var applicationNumber = " app-001";
        var service = CreateService(parent.Path);
        var expectedPath = Path.Combine(parent.Path, applicationNumber);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnsureApplicationWorkingDirectoryAsync(parent.Path, applicationNumber));

        Assert.False(Directory.Exists(expectedPath));
    }

    [Fact]
    public async Task EnsureSequenceWorkingDirectoryAsync_RejectsNonCanonicalPathSegment()
    {
        using var parent = new TemporaryDirectory();
        var applicationPath = Path.Combine(parent.Path, " app-001");
        Directory.CreateDirectory(applicationPath);
        var sequenceNumber = " 0001";
        var service = CreateService(parent.Path);
        var expectedPath = Path.Combine(applicationPath, sequenceNumber);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnsureSequenceWorkingDirectoryAsync(applicationPath, sequenceNumber));

        Assert.False(Directory.Exists(expectedPath));
    }

    [Fact]
    public async Task EnsureApplicationWorkingDirectoryAsync_RejectsTraversalPathSegment()
    {
        using var parent = new TemporaryDirectory();
        var service = CreateService(parent.Path);
        var escapingNumber = Path.Combine("..", $"escape-{Guid.NewGuid():N}");
        var escapedPath = Path.GetFullPath(Path.Combine(parent.Path, escapingNumber));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnsureApplicationWorkingDirectoryAsync(parent.Path, escapingNumber));
        Assert.False(Directory.Exists(escapedPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-workspace-{Guid.NewGuid():N}");
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
