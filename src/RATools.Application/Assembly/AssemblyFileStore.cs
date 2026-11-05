using System.Security.Cryptography;
using RATools.Application.Abstractions.Security;
using RATools.Application.Documents;
using RATools.Domain.Applications;
using RATools.Domain.Common;
using RATools.Domain.Documents;

namespace RATools.Application.Assembly;

public sealed class AssemblyExecutionOptions
{
    public long MaxFileBytes { get; set; } = 512 * 1024 * 1024;
    public long MaxBatchBytes { get; set; } = 1024L * 1024 * 1024;
    public TimeSpan PlanLifetime { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class AssemblyFileStore(IWorkspacePathPolicy paths, IDocumentStorageBoundary boundary, AssemblyExecutionOptions options)
{
    public async Task<SubmissionDocument> ReceiveAsync(SubmissionApplication application, string sequence, string fileName,
        Stream content, CancellationToken cancellationToken = default)
    {
        fileName = PortablePathSegment.NormalizeAndValidate(fileName, nameof(fileName));
        if (fileName.Length > 128 || !EctdDocumentFileRules.IsAllowedFileName(fileName)) throw new ArgumentException("Use a portable supported document filename of at most 128 characters.", nameof(fileName));
        var destination = boundary.EnsurePathOwnedBySequence(Path.Combine(application.WorkingDirectoryPath, sequence,
            "_assembly-sources", Guid.NewGuid().ToString("N"), fileName), application, sequence);
        EnsureNoReparse(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); EnsureNoReparse(destination);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[81920]; long length = 0; int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            length += read; if (length > options.MaxFileBytes) throw new InvalidDataException("The assembly source exceeds its file size limit.");
            sha.AppendData(buffer, 0, read); md5.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (length == 0) throw new InvalidDataException("An assembly source must not be empty.");
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new(fileName, EctdDocumentFileRules.GetMediaType(fileName), length, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(), destination);
    }
    public bool IsSource(SubmissionApplication application, string sequence, SubmissionDocument document)
    {
        var prefix = Path.GetFullPath(Path.Combine(application.WorkingDirectoryPath, sequence, "_assembly-sources")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(document.StoragePath).StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        _ = boundary.EnsureDocumentOwnedBySequence(document, application, sequence); return true;
    }
    public async Task<DocumentVersion> CaptureAsync(SubmissionApplication application, string sequence, SubmissionDocument document,
        CancellationToken cancellationToken = default)
    {
        var source = boundary.EnsureDocumentOwnedBySequence(document, application, sequence);
        var directory = paths.EnsureAllowed(Path.Combine(application.WorkingDirectoryPath, "_document-versions", "assembly", Guid.NewGuid().ToString("N")));
        EnsureNoReparse(source); EnsureNoReparse(directory);
        Directory.CreateDirectory(directory);
        var destination = paths.EnsureAllowed(Path.Combine(directory, "source" + Path.GetExtension(document.FileName)));
        await CopyAsync(source, destination, document.FileSize, document.Sha256, document.Md5, cancellationToken).ConfigureAwait(false);
        return DocumentVersion.CreateSource(document.Id, application.Id, sequence, document.FileName, document.MediaType,
            document.FileSize, document.Sha256, document.Md5, destination, DateTime.UtcNow);
    }
    public async Task<SubmissionDocument> PrepareAsync(SubmissionApplication application, string sequence, Guid jobId,
        DocumentVersion source, string href, CancellationToken cancellationToken = default)
    {
        if (source.ApplicationId != application.Id || source.SequenceNumber != sequence || jobId == Guid.Empty ||
            string.IsNullOrWhiteSpace(href) || href.Contains('\\') || Path.IsPathRooted(href) || href.Contains(':') ||
            href.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("Invalid assembly output binding.");
        var destination = boundary.EnsurePathOwnedBySequence(Path.Combine(application.WorkingDirectoryPath, sequence,
            "_assembled", jobId.ToString("N"), Guid.NewGuid().ToString("N"), sequence, href.Replace('/', Path.DirectorySeparatorChar)), application, sequence);
        EnsureNoReparse(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await CopyAsync(source.StoragePath, destination, source.FileSize, source.Sha256, source.Md5, cancellationToken).ConfigureAwait(false);
        return new(Path.GetFileName(href), source.MediaType, source.FileSize, source.Sha256, source.Md5, destination);
    }
    public async Task VerifyAsync(DocumentVersion source, CancellationToken cancellationToken = default) =>
        await VerifyAsync(source.StoragePath, source.FileSize, source.Sha256, source.Md5, cancellationToken).ConfigureAwait(false);
    public Task VerifyDocumentAsync(SubmissionDocument document, CancellationToken cancellationToken = default) =>
        VerifyAsync(document.StoragePath, document.FileSize, document.Sha256, document.Md5, cancellationToken);
    private async Task VerifyAsync(string source, long length, string sha, string md5, CancellationToken cancellationToken)
    {
        EnsureNoReparse(source);
        await using var stream = new FileStream(paths.EnsureAllowed(source), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await ReadAsync(stream, null, length, sha, md5, cancellationToken).ConfigureAwait(false);
    }
    private async Task CopyAsync(string source, string destination, long length, string sha, string md5, CancellationToken cancellationToken)
    {
        EnsureNoReparse(source); EnsureNoReparse(destination);
        await using var input = new FileStream(paths.EnsureAllowed(source), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var output = new FileStream(paths.EnsureAllowed(destination), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await ReadAsync(input, output, length, sha, md5, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    private static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Assembly paths must not contain symbolic links or reparse points.");
            current = Path.GetDirectoryName(current);
        }
    }
    private async Task ReadAsync(Stream source, Stream? output, long expectedLength, string expectedSha, string expectedMd5, CancellationToken cancellationToken)
    {
        if (expectedLength is < 0 || expectedLength > options.MaxFileBytes || source.Length != expectedLength)
            throw new InvalidDataException("Assembly source length changed or exceeds its limit.");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[81920]; long length = 0; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            length += read; if (length > options.MaxFileBytes || length > expectedLength) throw new InvalidDataException("Assembly source grew beyond its bound.");
            sha.AppendData(buffer, 0, read); md5.AppendData(buffer, 0, read);
            if (output is not null) await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (length != expectedLength || !string.Equals(Convert.ToHexString(sha.GetHashAndReset()), expectedSha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Convert.ToHexString(md5.GetHashAndReset()), expectedMd5, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Assembly source bytes changed; regenerate the preview.");
    }
}
