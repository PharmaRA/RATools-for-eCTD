namespace RATools.Application.Abstractions.Storage;

public interface IServerDirectoryBrowser
{
    DirectoryBrowseResult Browse(string? path);

    DirectoryResolutionResult Resolve(string path);
}

public sealed record DirectoryBrowseResult(string? CurrentPath, string? ParentPath, IReadOnlyCollection<DirectoryBrowseEntry> Directories);

public sealed record DirectoryBrowseEntry(string Name, string FullPath, bool CanBrowse, bool HasChildren);

public sealed record DirectoryResolutionResult(string FullPath, bool Exists, bool IsDirectory, bool IsAccessible);
