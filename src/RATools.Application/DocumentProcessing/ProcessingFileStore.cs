using RATools.Application.Abstractions.Security;
using RATools.Application.Documents;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using System.Security.Cryptography;

namespace RATools.Application.DocumentProcessing;

public sealed class PdfProcessingExecutionOptions
{
    public long MaxFileBytes { get; set; } = 512 * 1024 * 1024;
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class ProcessingFileStore
{
    private readonly IWorkspacePathPolicy _paths;
    private readonly PdfProcessingExecutionOptions _options;
    public ProcessingFileStore(IWorkspacePathPolicy paths, PdfProcessingExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFileBytes);
        _paths = paths;
        _options = options;
    }
    public async Task<DocumentVersion> CaptureAsync(SubmissionApplication application, string sequence, SubmissionDocument document,
        CancellationToken cancellationToken = default)
    {
        var source = new DocumentStorageBoundary(_paths).EnsureDocumentOwnedBySequence(document, application, sequence);
        var directory = NewVersionDirectory(application);
        var destination = _paths.EnsureAllowed(Path.Combine(directory, "source.pdf"));
        var measured = await CopyVerifiedAsync(source, destination, document.FileSize, document.Sha256, document.Md5, cancellationToken).ConfigureAwait(false);
        return DocumentVersion.CreateSource(document.Id, application.Id, sequence, document.FileName, document.MediaType,
            measured.Length, measured.Sha256, measured.Md5, destination, DateTime.UtcNow);
    }

    public string CreateAttemptOutput(SubmissionApplication application, Guid jobId, Guid attemptId)
    {
        if (jobId == Guid.Empty || attemptId == Guid.Empty) throw new ArgumentException("Task and attempt identities are required.");
        var directory = _paths.EnsureAllowed(Path.Combine(application.WorkingDirectoryPath, "_document-versions",
            "processing", jobId.ToString("N"), attemptId.ToString("N")));
        if (Directory.Exists(directory)) throw new IOException("Processing attempt directory already exists.");
        Directory.CreateDirectory(directory);
        return _paths.EnsureAllowed(Path.Combine(directory, "result.pdf"));
    }

    public async Task VerifyAsync(DocumentVersion version, CancellationToken cancellationToken = default)
    {
        var measured = await MeasureAsync(version.StoragePath, cancellationToken).ConfigureAwait(false);
        EnsureMatches(measured, version.FileSize, version.Sha256, version.Md5);
    }

    public async Task<SubmissionDocument> MaterializeAsync(SubmissionApplication application, string sequence, Guid jobId,
        DocumentVersion version, string href, CancellationToken cancellationToken = default)
    {
        if (version.ApplicationId != application.Id || version.SequenceNumber != sequence || jobId == Guid.Empty)
            throw new ArgumentException("Result version belongs to a different workspace.");
        if (string.IsNullOrWhiteSpace(href) || Path.IsPathRooted(href) || href.Contains('\\') || href.Contains(':') ||
            href.Split('/').Any(segment => segment is "" or "." or "..") || !href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A normalized relative PDF delivery path is required.", nameof(href));
        // A second sequence segment keeps the established publish-path resolver's
        // last-sequence rule while isolating the editable copy from originals.
        var destination = Path.Combine(application.WorkingDirectoryPath, sequence, "_processed", jobId.ToString("N"),
            Guid.NewGuid().ToString("N"), sequence, href.Replace('/', Path.DirectorySeparatorChar));
        destination = new DocumentStorageBoundary(_paths).EnsurePathOwnedBySequence(destination, application, sequence);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        destination = _paths.EnsureAllowed(destination);
        var measured = await CopyVerifiedAsync(version.StoragePath, destination, version.FileSize, version.Sha256, version.Md5, cancellationToken).ConfigureAwait(false);
        return new SubmissionDocument(Path.GetFileName(href), version.MediaType, measured.Length, measured.Sha256, measured.Md5, destination);
    }

    public async Task<ProcessingFileHash> MeasureAsync(string path, CancellationToken cancellationToken = default)
    {
        var allowed = _paths.EnsureAllowed(path);
        await using var stream = new FileStream(allowed, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return await ReadHashAsync(stream, null, cancellationToken).ConfigureAwait(false);
    }

    private string NewVersionDirectory(SubmissionApplication application)
    {
        var directory = _paths.EnsureAllowed(Path.Combine(application.WorkingDirectoryPath, "_document-versions", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        return _paths.EnsureAllowed(directory);
    }

    private async Task<ProcessingFileHash> CopyVerifiedAsync(string source, string output, long length, string sha256, string md5,
        CancellationToken cancellationToken)
    {
        source = _paths.EnsureAllowed(source);
        output = _paths.EnsureAllowed(output);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        var measured = await ReadHashAsync(input, destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        EnsureMatches(measured, length, sha256, md5);
        return measured;
    }

    private async Task<ProcessingFileHash> ReadHashAsync(Stream input, Stream? output, CancellationToken cancellationToken)
    {
        if (input.Length > _options.MaxFileBytes) throw new InvalidDataException("PDF exceeds the processing file size limit.");
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            length += read;
            if (length > _options.MaxFileBytes) throw new InvalidDataException("PDF exceeds the processing file size limit.");
            sha256.AppendData(buffer, 0, read); md5.AppendData(buffer, 0, read);
            if (output is not null) await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return new(length, Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(), Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant());
    }

    private static void EnsureMatches(ProcessingFileHash measured, long length, string sha256, string md5)
    {
        if (length != measured.Length || !string.Equals(sha256, measured.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(md5, measured.Md5, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Document bytes no longer match the bound length and checksums.");
    }
}

public sealed record ProcessingFileHash(long Length, string Sha256, string Md5);
