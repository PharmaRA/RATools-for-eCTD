using System.Runtime.InteropServices;
using RATools.Application.Abstractions.Storage;

namespace RATools.Infrastructure.Storage;

public sealed class LocalServerDirectoryBrowser : IServerDirectoryBrowser
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
                GetParentPath(resolved.FullPath),
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

        var normalizedPath = Path.GetFullPath(path.Trim());
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

    private static DirectoryBrowseResult BrowseRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var entries = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => new DirectoryBrowseEntry(drive.Name, drive.RootDirectory.FullName, true, true))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new DirectoryBrowseResult(null, null, entries);
        }

        var rootPath = Path.GetPathRoot(Environment.CurrentDirectory) ?? Path.DirectorySeparatorChar.ToString();
        return new LocalServerDirectoryBrowser().Browse(rootPath);
    }

    private static DirectoryBrowseEntry CreateBrowseEntry(string directoryPath)
    {
        var normalizedPath = Path.GetFullPath(directoryPath);
        var hasChildren = false;

        try
        {
            using var enumerator = Directory.EnumerateDirectories(normalizedPath).GetEnumerator();
            hasChildren = enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException)
        {
            return new DirectoryBrowseEntry(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath)),
                normalizedPath,
                false,
                false);
        }
        catch (IOException)
        {
            return new DirectoryBrowseEntry(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath)),
                normalizedPath,
                false,
                false);
        }

        return new DirectoryBrowseEntry(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath)),
            normalizedPath,
            true,
            hasChildren);
    }

    private static string? GetParentPath(string fullPath)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(fullPath);
        var parent = Directory.GetParent(normalizedPath);
        return parent?.FullName;
    }
}
