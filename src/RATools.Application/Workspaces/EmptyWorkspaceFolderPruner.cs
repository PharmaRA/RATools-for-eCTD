namespace RATools.Application.Workspaces;

internal static class EmptyWorkspaceFolderPruner
{
    public static void TryPruneBranches(IEnumerable<string?> startDirectories, string scopeRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(scopeRoot) || !Path.IsPathFullyQualified(scopeRoot))
            {
                return;
            }

            var normalizedScopeRoot = WorkspacePathGuard.Normalize(scopeRoot);
            var directories = startDirectories
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
                .Select(path => WorkspacePathGuard.Normalize(path!))
                .Where(path => WorkspacePathGuard.IsInsideScope(path, normalizedScopeRoot))
                .Where(path => !string.Equals(path, normalizedScopeRoot, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => path.Length)
                .ToArray();

            foreach (var directory in directories)
            {
                TryPruneBranch(directory, normalizedScopeRoot);
            }
        }
        catch
        {
            // Best-effort cleanup: callers should not fail when empty-folder pruning is blocked.
        }
    }

    private static void TryPruneBranch(string startDirectory, string scopeRoot)
    {
        var currentDirectory = startDirectory;

        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            var normalizedCurrentDirectory = WorkspacePathGuard.Normalize(currentDirectory);
            if (!WorkspacePathGuard.IsInsideScope(normalizedCurrentDirectory, scopeRoot)
                || string.Equals(normalizedCurrentDirectory, scopeRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!Directory.Exists(normalizedCurrentDirectory))
            {
                currentDirectory = Path.GetDirectoryName(normalizedCurrentDirectory);
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(normalizedCurrentDirectory).Any())
            {
                break;
            }

            Directory.Delete(normalizedCurrentDirectory, recursive: false);
            currentDirectory = Path.GetDirectoryName(normalizedCurrentDirectory);
        }
    }
}
