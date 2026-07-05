using RATools.Application.Workspaces;

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
    public async Task EmptyWorkspaceFolderPruner_RemovesOnlyEmptyDirectoriesInsideScope()
    {
        var scopeRoot = Path.Combine(Path.GetTempPath(), $"workspace-prune-{Guid.NewGuid():N}", "0001");
        var emptyLeaf = Path.Combine(scopeRoot, "m1", "us", "11-forms");
        var occupiedSibling = Path.Combine(scopeRoot, "m1", "us", "12-cover");
        Directory.CreateDirectory(emptyLeaf);
        Directory.CreateDirectory(occupiedSibling);
        await File.WriteAllTextAsync(Path.Combine(occupiedSibling, "cover.pdf"), "payload");

        try
        {
            EmptyWorkspaceFolderPruner.TryPruneBranches([emptyLeaf], scopeRoot);

            Assert.False(Directory.Exists(emptyLeaf));
            Assert.True(Directory.Exists(occupiedSibling));
            Assert.True(Directory.Exists(Path.Combine(scopeRoot, "m1", "us")));
            Assert.True(Directory.Exists(scopeRoot));
        }
        finally
        {
            if (Directory.Exists(scopeRoot))
            {
                Directory.Delete(scopeRoot, recursive: true);
            }
        }
    }
}
