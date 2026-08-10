using RATools.Application.Abstractions.Security;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public sealed class DocumentStorageBoundary(IWorkspacePathPolicy workspacePathPolicy) : IDocumentStorageBoundary
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public string EnsureAllowedDocumentPath(SubmissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return EnsureAllowedPath(document.StoragePath, $"Document {document.Id} storage path");
    }

    public string EnsureDocumentOwnedBySequence(
        SubmissionDocument document,
        SubmissionApplication application,
        string sequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        var documentPath = EnsurePathOwnedBySequence(document.StoragePath, application, sequenceNumber);
        var sequenceRoot = Normalize(Path.Combine(application.WorkingDirectoryPath, sequenceNumber));

        if (string.Equals(documentPath, sequenceRoot, PathComparison))
        {
            throw new DocumentStorageBoundaryException(
                $"Document {document.Id} storage path must identify a file under sequence {sequenceNumber}.");
        }

        return documentPath;
    }

    public string EnsurePathOwnedBySequence(
        string path,
        SubmissionApplication application,
        string sequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        if (application.Sequences.All(x => x.SequenceNumber != sequenceNumber))
        {
            throw new DocumentStorageBoundaryException(
                $"Sequence {sequenceNumber} does not exist on application {application.Id}.");
        }

        var applicationRoot = EnsureAllowedPath(
            application.WorkingDirectoryPath,
            $"Application {application.Id} workspace path");
        var sequenceRoot = EnsureAllowedPath(
            Path.Combine(applicationRoot, sequenceNumber),
            $"Application {application.Id} sequence {sequenceNumber} workspace path");

        if (!IsStrictlyInside(sequenceRoot, applicationRoot))
        {
            throw new DocumentStorageBoundaryException(
                $"Sequence {sequenceNumber} workspace path is outside application {application.Id}.");
        }

        var allowedPath = EnsureAllowedPath(path, "Document workspace path");
        if (!IsInsideOrEqual(allowedPath, sequenceRoot))
        {
            throw new DocumentStorageBoundaryException(
                $"Document workspace path is not owned by application {application.Id} sequence {sequenceNumber}.");
        }

        return allowedPath;
    }

    private string EnsureAllowedPath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new DocumentStorageBoundaryException($"{description} must be fully qualified.");
        }

        try
        {
            return workspacePathPolicy.EnsureAllowed(path);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or InvalidOperationException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            throw new DocumentStorageBoundaryException($"{description} is outside the approved workspace boundary.", exception);
        }
    }

    private static bool IsInsideOrEqual(string path, string scopeRoot)
    {
        var normalizedPath = Normalize(path);
        var normalizedRoot = Normalize(scopeRoot);
        return string.Equals(normalizedPath, normalizedRoot, PathComparison)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool IsStrictlyInside(string path, string scopeRoot)
    {
        var normalizedPath = Normalize(path);
        var normalizedRoot = Normalize(scopeRoot);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

public sealed class DocumentStorageBoundaryException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
