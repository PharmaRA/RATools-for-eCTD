using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Infrastructure.Security;

namespace RATools.Infrastructure.Storage;

public sealed class ConfiguredWorkspacePathPolicy : IWorkspacePathPolicy
{
    private readonly string[] _allowedRoots;
    private readonly StringComparison _pathComparison;

    public ConfiguredWorkspacePathPolicy(IOptions<SecurityOptions> options)
    {
        _pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        _allowedRoots = options.Value.AllowedWorkspaceRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Normalize)
            .Distinct(_pathComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<string> GetAllowedRoots() => _allowedRoots;

    public string EnsureAllowed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_allowedRoots.Length == 0)
        {
            throw new InvalidOperationException("No allowed workspace roots are configured.");
        }

        var normalizedPath = Normalize(path);
        var matchingRoot = _allowedRoots.FirstOrDefault(root => IsInsideRoot(normalizedPath, root));
        if (matchingRoot is not null)
        {
            EnsureNoReparsePointDirectories(normalizedPath, matchingRoot);
            EnsureFileIsNotReparsePoint(normalizedPath);
            return normalizedPath;
        }

        throw new InvalidOperationException($"Path '{normalizedPath}' is outside the configured workspace roots.");
    }

    private bool IsInsideRoot(string normalizedPath, string normalizedRoot)
    {
        if (string.Equals(normalizedPath, normalizedRoot, _pathComparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, _pathComparison);
    }

    private static void EnsureNoReparsePointDirectories(string normalizedPath, string normalizedRoot)
    {
        var currentPath = normalizedRoot;
        if (!EnsureDirectoryIsNotReparsePoint(currentPath))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(normalizedRoot, normalizedPath);
        if (relativePath == ".")
        {
            return;
        }

        foreach (var part in relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, part);
            if (!EnsureDirectoryIsNotReparsePoint(currentPath))
            {
                return;
            }
        }
    }

    private static bool EnsureDirectoryIsNotReparsePoint(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            throw new InvalidOperationException($"Path '{path}' contains a reparse point directory, which is not allowed in workspace roots.");
        }

        return true;
    }

    private static void EnsureFileIsNotReparsePoint(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            throw new InvalidOperationException($"Path '{path}' is a reparse point file, which is not allowed in workspace roots.");
        }
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
