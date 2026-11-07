using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;

namespace RATools.Infrastructure.Storage;

/// <summary>
/// Retains removed workspaces in a sibling recycle directory. Both source and
/// destination must satisfy <see cref="IWorkspacePathPolicy.EnsureAllowed"/>;
/// linked trees are rejected before moving any directory.
/// </summary>
public sealed class WorkspaceDeletionService(IWorkspacePathPolicy workspacePathPolicy) : IWorkspaceDeletionService
{
    public Task DeleteApplicationWorkspaceAsync(string applicationWorkingDirectoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);

        var fullPath = NormalizeRemovalTarget(applicationWorkingDirectoryPath);
        workspacePathPolicy.EnsureAllowed(fullPath);
        RecycleDirectory(fullPath, cancellationToken);

        return Task.CompletedTask;
    }

    public Task DeleteSequenceWorkspaceAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationWorkingDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        var fullApplicationPath = NormalizeRemovalTarget(applicationWorkingDirectoryPath);
        workspacePathPolicy.EnsureAllowed(fullApplicationPath);
        var fullSequencePath = NormalizeRemovalTarget(Path.Combine(fullApplicationPath, sequenceNumber));
        workspacePathPolicy.EnsureAllowed(fullSequencePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(fullSequencePath), Path.TrimEndingDirectorySeparator(fullApplicationPath), comparison) ||
            string.Equals(Path.GetFileName(fullSequencePath), "_recycle", comparison))
            throw new InvalidOperationException("Sequence removal must target a direct application child outside recycle.");
        RecycleDirectory(fullSequencePath, cancellationToken);

        return Task.CompletedTask;
    }

    private void RecycleDirectory(string source, CancellationToken cancellationToken)
    {
        source = NormalizeRemovalTarget(source);
        EnsureNoLinks(source);
        if (!Directory.Exists(source)) return;
        var parent = Path.GetDirectoryName(source) ?? throw new InvalidOperationException("Cannot recycle a filesystem root.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFileName(source), "_recycle", comparison))
            throw new InvalidOperationException("Cannot recycle the retention directory itself.");
        var recycle = Path.Combine(parent, "_recycle");
        workspacePathPolicy.EnsureAllowed(recycle);
        EnsureNoLinks(recycle);
        var pending = new Stack<string>(); pending.Push(source);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoLinks(current);
            if (Directory.Exists(current))
                foreach (var child in Directory.EnumerateFileSystemEntries(current)) pending.Push(child);
        }
        Directory.CreateDirectory(recycle); EnsureNoLinks(recycle);
        var destination = Path.Combine(recycle, Guid.NewGuid().ToString("N"));
        workspacePathPolicy.EnsureAllowed(destination);
        if (!string.Equals(Path.GetDirectoryName(destination), recycle, comparison))
            throw new InvalidOperationException("Workspace recycle destination escaped.");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoLinks(source);
        Directory.Move(source, destination);
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Workspace retention paths must not traverse links.");
    }

    private static string NormalizeRemovalTarget(string path)
    {
        var absolute = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.TrimEndingDirectorySeparator(absolute), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(absolute)!), comparison))
            throw new InvalidOperationException("Cannot recycle a filesystem root.");
        return Path.TrimEndingDirectorySeparator(absolute);
    }
}
