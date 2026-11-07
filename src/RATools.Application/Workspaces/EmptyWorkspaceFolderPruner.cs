namespace RATools.Application.Workspaces;

internal static class EmptyWorkspaceFolderPruner
{
    public static void TryPruneBranches(IEnumerable<string?> startDirectories, string scopeRoot)
    {
        // Optional pruning is intentionally disabled. Physical deletion is not
        // permitted, and moving a directory after an empty check could relocate
        // files created by an ordinary concurrent writer. Retain directories in
        // place without enumerating candidates or touching the filesystem. The
        // compatibility entry point lets document operations keep their existing
        // best-effort cleanup contract.
    }
}
