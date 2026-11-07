using RATools.Application.Workspaces;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.Application;

public sealed class WorkspacePathGuardTests
{
    [Fact]
    public void IsInsideScope_ReturnsFalseForSiblingWithSharedPrefix()
    {
        var scopeRoot = Path.Combine(Path.GetTempPath(), $"workspace-{Guid.NewGuid():N}", "app");
        var sibling = scopeRoot + "-archive";

        Assert.True(WorkspacePathGuard.IsInsideScope(Path.Combine(scopeRoot, "0001", "index.xml"), scopeRoot));
        Assert.True(WorkspacePathGuard.IsInsideScope(scopeRoot, scopeRoot));
        Assert.False(WorkspacePathGuard.IsInsideScope(sibling, scopeRoot));
    }

    [Fact]
    public async Task DisabledPruningPreservesEmptyAndOccupiedBranchesInsideScope()
    {
        var owned = RetainedPackageTestFiles.Create();
        var scopeRoot = Path.Combine(owned, "0001");
        var emptyLeaf = Path.Combine(scopeRoot, "m1", "us", "11-forms");
        var occupiedSibling = Path.Combine(scopeRoot, "m1", "us", "12-cover");
        Directory.CreateDirectory(emptyLeaf);
        Directory.CreateDirectory(occupiedSibling);
        await File.WriteAllTextAsync(Path.Combine(occupiedSibling, "cover.pdf"), "payload");

        try
        {
            EmptyWorkspaceFolderPruner.TryPruneBranches([emptyLeaf], scopeRoot);

            Assert.True(Directory.Exists(emptyLeaf));
            Assert.True(Directory.Exists(occupiedSibling));
            Assert.True(Directory.Exists(Path.Combine(scopeRoot, "m1", "us")));
            Assert.True(Directory.Exists(scopeRoot));
            Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(occupiedSibling, "cover.pdf")));
            Assert.False(Directory.Exists(Path.Combine(scopeRoot, "_recycle")));
        }
        finally
        {
            RetainedPackageTestFiles.RetainDirectory(owned);
        }
    }

    [Fact]
    public void DisabledPruningPreservesEmptyAncestorsAndExistingRecycle()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var leaf = Path.Combine(root, "a", "b", "c"); Directory.CreateDirectory(leaf);
            var recycle = Path.Combine(root, "_recycle");
            var retained = Path.Combine(recycle, "existing"); Directory.CreateDirectory(retained);
            EmptyWorkspaceFolderPruner.TryPruneBranches([leaf, root], root);
            Assert.True(Directory.Exists(leaf));
            Assert.True(Directory.Exists(root));
            EmptyWorkspaceFolderPruner.TryPruneBranches([retained, recycle], root);
            Assert.Equal(retained, Assert.Single(Directory.GetDirectories(recycle)));
            Assert.Empty(Directory.GetFileSystemEntries(retained));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public void OutOfScopeAndRelativeCandidatesAreIgnored()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var scope = Path.Combine(root, "app"); var outside = scope + "-archive";
            Directory.CreateDirectory(scope); Directory.CreateDirectory(outside);
            EmptyWorkspaceFolderPruner.TryPruneBranches([outside, "relative", null, scope], scope);
            Assert.True(Directory.Exists(outside)); Assert.Empty(Directory.GetFileSystemEntries(scope));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public async Task BlockedRecyclePreservesEmptyBranchAndExistingBytes()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var leaf = Path.Combine(root, "empty"); Directory.CreateDirectory(leaf);
            await File.WriteAllTextAsync(Path.Combine(root, "_recycle"), "blocked");
            EmptyWorkspaceFolderPruner.TryPruneBranches([leaf], root);
            Assert.True(Directory.Exists(leaf));
            Assert.Equal("blocked", await File.ReadAllTextAsync(Path.Combine(root, "_recycle")));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledPruningDoesNotFollowSourceOrRecycleLinks(bool recycleLink)
    {
        var root = RetainedPackageTestFiles.Create();
        var scope = Path.Combine(root, "scope"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(scope); Directory.CreateDirectory(target);
        var leaf = Path.Combine(scope, "empty"); Directory.CreateDirectory(leaf);
        var link = Path.Combine(scope, recycleLink ? "_recycle" : "linked");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            EmptyWorkspaceFolderPruner.TryPruneBranches([recycleLink ? leaf : link], scope);
            Assert.True(Directory.Exists(leaf)); Assert.True(Directory.Exists(link));
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally
        {
            RetainedPackageTestFiles.RetainReviewedLink(root, link, target);
            RetainedPackageTestFiles.RetainDirectory(root);
        }
    }

    [Fact]
    public void FilesystemRootScopeIsRejectedBeforeEnumeratingCandidates()
    {
        var candidates = new UnreadableCandidates();
        EmptyWorkspaceFolderPruner.TryPruneBranches(candidates, Path.GetPathRoot(Path.GetFullPath("."))!);
        Assert.False(candidates.WasEnumerated);
    }

    private sealed class UnreadableCandidates : IEnumerable<string?>
    {
        public bool WasEnumerated { get; private set; }
        public IEnumerator<string?> GetEnumerator() { WasEnumerated = true; throw new IOException("Safe root probe"); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
