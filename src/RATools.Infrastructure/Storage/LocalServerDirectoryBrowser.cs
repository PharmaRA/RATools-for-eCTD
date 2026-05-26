using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;

namespace RATools.Infrastructure.Storage;

public sealed class LocalServerDirectoryBrowser(IWorkspacePathPolicy workspacePathPolicy) : IServerDirectoryBrowser
{
    public DirectoryBrowseResult Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BrowseRoot();
        }

        var resolved = Resolve(path);

        try
        {
            var entries = Directory.EnumerateDirectories(resolved.FullPath)
                .Select(CreateBrowseEntry)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new DirectoryBrowseResult(
                resolved.FullPath,
                GetAllowedParentPath(resolved.FullPath),
                entries);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Directory '{resolved.FullPath}' is inaccessible.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Directory '{resolved.FullPath}' is inaccessible.", exception);
        }
    }

    public DirectoryResolutionResult Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = workspacePathPolicy.EnsureAllowed(path);
        if (File.Exists(normalizedPath))
        {
            throw new InvalidOperationException($"Path '{normalizedPath}' is a file, not a directory.");
        }

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Directory '{normalizedPath}' was not found.");
        }

        try
        {
            using var enumerator = Directory.EnumerateDirectories(normalizedPath).GetEnumerator();
            _ = enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Directory '{normalizedPath}' is inaccessible.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Directory '{normalizedPath}' is inaccessible.", exception);
        }

        return new DirectoryResolutionResult(normalizedPath, true, true, true);
    }

    private DirectoryBrowseResult BrowseRoot()
    {
        var allowedRoots = workspacePathPolicy.GetAllowedRoots();
        if (allowedRoots.Count == 0)
        {
            workspacePathPolicy.EnsureAllowed(Environment.CurrentDirectory);
        }

        var entries = allowedRoots
            .Select(CreateRootBrowseEntry)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DirectoryBrowseResult(null, null, entries);
    }

    private DirectoryBrowseEntry CreateRootBrowseEntry(string directoryPath)
    {
        var normalizedPath = Path.GetFullPath(directoryPath);
        try
        {
            normalizedPath = workspacePathPolicy.EnsureAllowed(normalizedPath);
        }
        catch (InvalidOperationException)
        {
            return CreateInaccessibleEntry(normalizedPath);
        }

        if (Directory.Exists(normalizedPath))
        {
            return CreateBrowseEntry(normalizedPath);
        }

        return CreateInaccessibleEntry(normalizedPath);
    }

    private DirectoryBrowseEntry CreateBrowseEntry(string directoryPath)
    {
        string normalizedPath;
        try
        {
            normalizedPath = workspacePathPolicy.EnsureAllowed(directoryPath);
        }
        catch (InvalidOperationException)
        {
            normalizedPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return CreateInaccessibleEntry(normalizedPath);
        }

        var hasChildren = false;

        try
        {
            using var enumerator = Directory.EnumerateDirectories(normalizedPath).GetEnumerator();
            hasChildren = enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException)
        {
            return CreateInaccessibleEntry(normalizedPath);
        }
        catch (IOException)
        {
            return CreateInaccessibleEntry(normalizedPath);
        }

        return new DirectoryBrowseEntry(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath)),
            normalizedPath,
            true,
            hasChildren);
    }

    private static DirectoryBrowseEntry CreateInaccessibleEntry(string normalizedPath)
    {
        return new DirectoryBrowseEntry(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath)),
            normalizedPath,
            false,
            false);
    }

    private string? GetAllowedParentPath(string fullPath)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(fullPath);
        var parent = Directory.GetParent(normalizedPath);
        if (parent is null)
        {
            return null;
        }

        try
        {
            return workspacePathPolicy.EnsureAllowed(parent.FullName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
