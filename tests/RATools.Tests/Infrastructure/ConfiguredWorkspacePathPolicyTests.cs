using RATools.Tests.PackageValidation;
using Microsoft.Extensions.Options;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Infrastructure;

public sealed class ConfiguredWorkspacePathPolicyTests
{
    [Fact]
    public void EnsureAllowed_Throws_WhenNoRootsAreConfigured()
    {
        var policy = CreatePolicy([]);

        var exception = Assert.Throws<InvalidOperationException>(() => policy.EnsureAllowed(Path.GetTempPath()));

        Assert.Contains("No allowed workspace roots are configured", exception.Message);
    }

    [Fact]
    public void EnsureAllowed_ReturnsNormalizedPath_ForConfiguredRoot()
    {
        using var root = new TemporaryDirectory();
        var policy = CreatePolicy([root.Path]);

        var resolved = policy.EnsureAllowed(root.Path);

        Assert.Equal(Path.GetFullPath(root.Path), resolved);
    }

    [Fact]
    public void EnsureAllowed_ReturnsNormalizedPath_ForChildDirectory()
    {
        using var root = new TemporaryDirectory();
        var child = Path.Combine(root.Path, "child");
        Directory.CreateDirectory(child);
        var policy = CreatePolicy([root.Path]);

        var resolved = policy.EnsureAllowed(child);

        Assert.Equal(Path.GetFullPath(child), resolved);
    }

    [Fact]
    public void EnsureAllowed_PreservesLeadingSpaceDirectoryName()
    {
        using var parent = new TemporaryDirectory();
        var rootPath = Path.Combine(parent.Path, " root");
        Directory.CreateDirectory(rootPath);

        var policy = CreatePolicy([rootPath]);

        var resolved = policy.EnsureAllowed(rootPath);

        Assert.Equal(Path.GetFullPath(rootPath), resolved);
    }

    [Fact]
    public void EnsureAllowed_RejectsSiblingWithSharedPrefix()
    {
        var parent = RetainedPackageTestFiles.Create();
        var rootPath = Path.Combine(parent, "root");
        var siblingPath = Path.Combine(parent, "root-sibling");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(siblingPath);

        try
        {
            var policy = CreatePolicy([rootPath]);

            var exception = Assert.Throws<InvalidOperationException>(() => policy.EnsureAllowed(siblingPath));

            Assert.Contains("outside the configured workspace roots", exception.Message);
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                RetainedPackageTestFiles.RetainDirectory(parent);
            }
        }
    }

    [Fact]
    public void EnsureAllowed_RejectsReparsePointInsideAllowedRoot()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var linkPath = Path.Combine(allowedRoot.Path, "link");
        using var ownedLink = RetainedPackageTestFiles.CreateOwnedLink(allowedRoot.Path, linkPath, outsideRoot.Path, outsideRoot.Path);

        var policy = CreatePolicy([allowedRoot.Path]);

        var exception = Assert.Throws<InvalidOperationException>(() => policy.EnsureAllowed(linkPath));

        Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureAllowed_RejectsFileReparsePointInsideAllowedRoot()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var targetPath = Path.Combine(outsideRoot.Path, "outside.txt");
        File.WriteAllText(targetPath, "outside content");
        var linkPath = Path.Combine(allowedRoot.Path, "link.txt");
        using var ownedLink = RetainedPackageTestFiles.CreateOwnedLink(allowedRoot.Path, linkPath, outsideRoot.Path, targetPath, directory: false);

        var policy = CreatePolicy([allowedRoot.Path]);

        var exception = Assert.Throws<InvalidOperationException>(() => policy.EnsureAllowed(linkPath));

        Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ConfiguredWorkspacePathPolicy CreatePolicy(string[] roots)
        => new(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = roots
        }));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = RetainedPackageTestFiles.Create();
            Directory.CreateDirectory(Path);
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
