namespace RATools.Application.Workspaces;

internal static class WorkspacePathGuard
{
    public static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsInsideScope(string? path, string scopeRoot)
    {
        if (string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(scopeRoot)
            || !Path.IsPathFullyQualified(path)
            || !Path.IsPathFullyQualified(scopeRoot))
        {
            return false;
        }

        var normalizedPath = Normalize(path);
        var normalizedRoot = Normalize(scopeRoot);
        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
