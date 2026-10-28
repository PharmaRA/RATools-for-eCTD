using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RATools.Application.Finalization;
using RATools.Domain.Common;
using RATools.Domain.Finalization;

namespace RATools.Infrastructure.Publishing;

// P4-02 storage boundary: a captured release is an independent copy inside the
// application output root. Nothing is hard-linked, so a later workspace edit can never
// change delivered bytes. Every copied byte is re-hashed here and compared with the
// planned digest; a mismatch is a source change, not something to adopt silently.
public sealed class LocalReleaseCaptureStore(IOptions<BackboneOutputOptions> options) : IReleaseCaptureStore
{
    private const string StagingDirectoryName = "_staging";

    public async Task<ReleaseCaptureStaging> SaveAsync(ReleaseCapturePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ApplicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(plan));
        var sequence = PortablePathSegment.NormalizeAndValidate(plan.SequenceNumber, nameof(plan));
        if (plan.Files.Count == 0) throw new ReleaseCapturePlanException("A captured release requires at least one file.");

        var applicationRoot = ResolveApplicationRoot(plan.ApplicationId);
        var stagingRoot = ResolveDescendantPath(applicationRoot, StagingDirectoryName, "Release staging root");
        var staging = ResolveDescendantPath(stagingRoot, "release-" + Guid.NewGuid().ToString("N"), "Release staging directory");
        EnsureNoReparsePoints(applicationRoot, staging);
        Directory.CreateDirectory(staging);
        var measured = new List<ReleaseManifestFile>(plan.Files.Count);
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in plan.Files)
            {
                ReleaseManifest.RequireRelativePath(file.RelativePath, nameof(plan));
                if (!seen.Add(file.RelativePath))
                    throw new ReleaseCapturePlanException($"Release path '{file.RelativePath}' is planned twice.");
                if ((file.SourcePath is null) == (file.Content is null))
                    throw new ReleaseCapturePlanException($"Release path '{file.RelativePath}' must have exactly one byte source.");
                var destination = ResolveDescendantPath(staging, file.RelativePath, "Release file path");
                EnsureNoReparsePoints(staging, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var (length, sha256, md5) = file.Content is { } content
                    ? await WriteBytesAsync(destination, content, cancellationToken).ConfigureAwait(false)
                    : await CopyAsync(file.SourcePath!, destination, cancellationToken).ConfigureAwait(false);
                if (length != file.Length || !string.Equals(sha256, file.Sha256, StringComparison.Ordinal) ||
                    !string.Equals(md5, file.Md5, StringComparison.Ordinal))
                    throw new ReleaseCaptureSourceChangedException(file.RelativePath);
                measured.Add(new ReleaseManifestFile(file.RelativePath, length, sha256, md5, file.Kind));
            }
            return new ReleaseCaptureStaging(plan.ApplicationId, sequence, staging, measured);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    public async Task CompleteAsync(ReleaseCaptureStaging staging, ReleaseManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ApplicationId != staging.ApplicationId ||
            !string.Equals(manifest.SequenceNumber, staging.SequenceNumber, StringComparison.Ordinal))
            throw new ArgumentException("The manifest must describe the staged application and sequence.", nameof(manifest));
        // The manifest must describe exactly the bytes that were written; otherwise the
        // snapshot would record a digest that no file on disk reproduces.
        if (manifest.Files.Count != staging.Files.Count ||
            manifest.Files.Any(file => !staging.Files.Contains(file)))
            throw new ArgumentException("The manifest must list exactly the files written to staging.", nameof(manifest));

        var manifestPath = ResolveDescendantPath(staging.Directory, "manifest.json", "Release manifest path");
        var bytes = manifest.ToUtf8Json();
        await File.WriteAllBytesAsync(manifestPath, bytes, cancellationToken).ConfigureAwait(false);
        // Reading back the exact file proves the canonical encoding round-trips: a digest
        // that could not be reproduced from disk would make every later verification fail.
        var reloaded = ReleaseManifest.FromJson(await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false),
            manifest.ApplicationId, manifest.SequenceNumber, manifest.Digest);
        if (!string.Equals(reloaded.Digest, manifest.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("The stored release manifest does not reproduce its committed digest.");

        // The delivery package is the payload exactly as recorded. It is built from the
        // captured files, not the workspace, so it cannot pick up a later edit, and its
        // own hash stays outside the manifest (the manifest is inside the package).
        var payloadRoot = ResolveDescendantPath(staging.Directory, "payload", "Release payload root");
        var packagePath = ResolveDescendantPath(staging.Directory, "delivery-package.zip", "Release package path");
        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
            foreach (var file in manifest.Files.Where(file => file.Kind == ReleaseFileKind.Payload)
                         .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
            {
                var entryName = file.RelativePath["payload/".Length..];
                var source = ResolveDescendantPath(payloadRoot, entryName, "Release payload entry");
                if (!File.Exists(source))
                    throw new InvalidDataException($"Captured payload file '{file.RelativePath}' is missing; the package cannot be built.");
                archive.CreateEntryFromFile(source, entryName, CompressionLevel.Optimal);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReleaseCommitResult> CommitAsync(ReleaseCaptureStaging staging, string manifestDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        cancellationToken.ThrowIfCancellationRequested();
        RequireStagingDirectory(staging);
        // The staged bytes must already be the release this digest names; moving a staging
        // directory whose manifest says otherwise would publish a misnamed release.
        var staged = ReadStoredManifest(staging.Directory, staging.ApplicationId, staging.SequenceNumber, manifestDigest);
        if (!string.Equals(staged.Digest, manifestDigest, StringComparison.Ordinal))
            throw new ReleaseCaptureConflictException(staging.Directory);
        var destination = ResolveReleaseDirectory(staging.ApplicationId, staging.SequenceNumber, manifestDigest);
        EnsureNoReparsePoints(ResolveApplicationRoot(staging.ApplicationId), destination);
        if (Directory.Exists(destination))
        {
            // The directory name is the content identity: an existing directory already
            // holds exactly these bytes and is never overwritten by a second capture.
            var existing = ReadStoredManifest(destination, staging.ApplicationId, staging.SequenceNumber, manifestDigest);
            if (!string.Equals(existing.Digest, manifestDigest, StringComparison.Ordinal))
                throw new ReleaseCaptureConflictException(destination);
            TryDelete(staging.Directory);
            return Task.FromResult(new ReleaseCommitResult(destination, Created: false));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            Directory.Move(staging.Directory, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException &&
            Directory.Exists(destination))
        {
            // A concurrent capture published the same content first; identical content is
            // the same release, so this capture adopts it instead of failing the snapshot.
            var winner = ReadStoredManifest(destination, staging.ApplicationId, staging.SequenceNumber, manifestDigest);
            if (!string.Equals(winner.Digest, manifestDigest, StringComparison.Ordinal))
                throw new ReleaseCaptureConflictException(destination);
            TryDelete(staging.Directory);
            return Task.FromResult(new ReleaseCommitResult(destination, Created: false));
        }
        return Task.FromResult(new ReleaseCommitResult(destination, Created: true));
    }

    // A staging path is data on a public record; it must still be a direct child of this
    // application's staging root before it is moved or recursively deleted.
    private void RequireStagingDirectory(ReleaseCaptureStaging staging)
    {
        var stagingRoot = ResolveDescendantPath(ResolveApplicationRoot(staging.ApplicationId), StagingDirectoryName,
            "Release staging root");
        var relative = Path.GetRelativePath(stagingRoot, Path.GetFullPath(staging.Directory));
        if (relative is "." or ".." || relative.StartsWith("..", StringComparison.Ordinal) ||
            relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !Directory.Exists(staging.Directory))
            throw new ReleaseCaptureConflictException(staging.Directory);
        EnsureNoReparsePoints(ResolveApplicationRoot(staging.ApplicationId), staging.Directory);
    }

    // Cleanup never honours request cancellation: a cancelled request must not leave
    // staging bytes behind, so only the containment check can refuse.
    public Task DiscardAsync(ReleaseCaptureStaging staging, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staging);
        var stagingRoot = ResolveDescendantPath(ResolveApplicationRoot(staging.ApplicationId), StagingDirectoryName,
            "Release staging root");
        // Only a directory directly below this application's staging root can be removed.
        var relative = Path.GetRelativePath(stagingRoot, Path.GetFullPath(staging.Directory));
        if (relative is "." or ".." || relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith("..", StringComparison.Ordinal))
            throw new ReleaseCaptureConflictException(staging.Directory);
        TryDelete(staging.Directory);
        return Task.CompletedTask;
    }

    // Removes a release directory only when the caller has proven no snapshot references
    // it. The caller is the finalization service, which checks the registered snapshots
    // under the application lock; a concurrent identical capture that already committed
    // makes this a no-op. Cleanup does not honour request cancellation.
    public Task DiscardCommittedAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
        CancellationToken cancellationToken = default)
    {
        var directory = ResolveReleaseDirectory(applicationId, sequenceNumber, manifestDigest);
        EnsureNoReparsePoints(ResolveApplicationRoot(applicationId), directory);
        TryDelete(directory);
        return Task.CompletedTask;
    }

    public async Task<ReleaseManifest?> ReadManifestAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
        CancellationToken cancellationToken = default)
    {
        var sequence = PortablePathSegment.NormalizeAndValidate(sequenceNumber, nameof(sequenceNumber));
        var directory = ResolveReleaseDirectory(applicationId, sequence, manifestDigest);
        var manifestPath = ResolveDescendantPath(directory, "manifest.json", "Release manifest path");
        if (!File.Exists(manifestPath)) return null;
        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return ReleaseManifest.FromJson(bytes, applicationId, sequence, manifestDigest);
    }

    // A preview reader addresses one file of a verified release. The manifest is re-read
    // and re-hashed first, so a tampered release cannot serve bytes; only then is the
    // member resolved inside the release directory.
    public async Task<ReleaseFileContent> OpenFileAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
        string relativePath, CancellationToken cancellationToken = default)
    {
        ReleaseManifest.RequireRelativePath(relativePath, nameof(relativePath));
        var manifest = await ReadManifestAsync(applicationId, sequenceNumber, manifestDigest, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ReleaseCaptureMissingException(manifestDigest);
        var file = manifest.Files.SingleOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.Ordinal))
            ?? throw new ReleaseFileNotFoundException(relativePath);
        var directory = ResolveReleaseDirectory(applicationId, sequenceNumber, manifestDigest);
        var path = ResolveDescendantPath(directory, file.RelativePath, "Release file path");
        if (!File.Exists(path)) throw new ReleaseFileNotFoundException(relativePath);
        // The bytes must still be the ones the manifest recorded; a modified preview file
        // is missing evidence, not a different version.
        var (length, sha256) = await MeasureFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (length != file.Length || !string.Equals(sha256, file.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Captured release file '{relativePath}' no longer matches its recorded digest.");
        return new ReleaseFileContent(path, file.RelativePath, length, sha256, MediaTypeFor(file.RelativePath));
    }

    // Preview content types are derived from the recorded path, never from a caller
    // header; an unknown extension is served as bytes so it is downloaded, not interpreted.
    private static string MediaTypeFor(string relativePath) => Path.GetExtension(relativePath).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xml" => "application/xml",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream"
    };

    // Only the recorded SHA-256 gates a preview, so the verification read hashes once.
    private static async Task<(long Length, string Sha256)> MeasureFileAsync(string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long length = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
            sha.AppendData(buffer, 0, read);
        }
        return (length, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    private string ResolveReleaseDirectory(Guid applicationId, string sequenceNumber, string manifestDigest)
    {
        RequireDigest(manifestDigest, nameof(manifestDigest));
        var sequence = PortablePathSegment.NormalizeAndValidate(sequenceNumber, nameof(sequenceNumber));
        var releasesRoot = ResolveDescendantPath(ResolveApplicationRoot(applicationId), "_releases", "Release root");
        var sequenceRoot = ResolveDescendantPath(releasesRoot, sequence, "Sequence release root");
        return ResolveDescendantPath(sequenceRoot, manifestDigest, "Release directory");
    }

    private static ReleaseManifest ReadStoredManifest(string directory, Guid applicationId, string sequenceNumber,
        string manifestDigest)
    {
        var manifestPath = ResolveDescendantPath(directory, "manifest.json", "Release manifest path");
        if (!File.Exists(manifestPath)) throw new ReleaseCaptureConflictException(directory);
        // The directory name is the expected identity; FromJson recomputes the digest from
        // the stored file and refuses a manifest that no longer reproduces it.
        return ReleaseManifest.FromJson(File.ReadAllBytes(manifestPath), applicationId, sequenceNumber, manifestDigest);
    }

    private string ResolveApplicationRoot(Guid applicationId)
    {
        var rootPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath)) throw new InvalidOperationException("Backbone output root path is not configured.");
        var fullRoot = Path.GetFullPath(rootPath);
        var applicationRoot = ResolveDescendantPath(fullRoot, applicationId.ToString("N"), "Application output root");
        EnsureNoReparsePoints(fullRoot, applicationRoot);
        return applicationRoot;
    }

    private static async Task<(long Length, string Sha256, string Md5)> WriteBytesAsync(string destination, byte[] content,
        CancellationToken cancellationToken)
    {
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
            FileOptions.Asynchronous))
            await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        return (content.Length, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant());
    }

    // Streams the source once: hashing and copying are the same read, so the recorded
    // digest cannot describe different bytes than the ones written.
    private static async Task<(long Length, string Sha256, string Md5)> CopyAsync(string sourcePath, string destination,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
            FileOptions.Asynchronous);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[65536];
        long length = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length = checked(length + read);
            sha256.AppendData(buffer, 0, read);
            md5.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (length != source.Length) throw new IOException($"Source '{sourcePath}' changed length while it was copied.");
        return (length, Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant());
    }

    private static void RequireDigest(string digest, string parameterName)
    {
        if (digest is null || digest.Length != 64 || digest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A release digest must contain 64 lowercase hexadecimal characters.", parameterName);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return;
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A staging directory that cannot be removed is never delivery evidence; the
            // next capture uses a fresh name. Never turn a cleanup failure into a failure
            // of the finalized snapshot.
        }
    }

    private static string ResolveDescendantPath(string rootPath, string relativePath, string pathDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith('/') || relativePath.StartsWith('\\') ||
            (relativePath.Length >= 2 && char.IsAsciiLetter(relativePath[0]) && relativePath[1] == ':'))
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' must not be rooted.");
        if (relativePath.Contains('\\'))
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' must use canonical forward-slash separators.");
        var segments = relativePath.Split('/');
        if (segments.Any(segment => string.IsNullOrEmpty(segment) ||
            !string.Equals(segment, PortablePathSegment.NormalizeAndValidate(segment, nameof(relativePath)), StringComparison.Ordinal)))
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' is not a portable relative path.");
        var fullRoot = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));
        if (!IsDescendantPath(fullRoot, fullPath))
            throw new InvalidOperationException($"{pathDescription} '{relativePath}' escapes '{fullRoot}'.");
        return fullPath;
    }

    private static bool IsDescendantPath(string rootPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(candidatePath));
        return relative != "." && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static void EnsureNoReparsePoints(string rootPath, string candidatePath)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        if (!IsDescendantPath(fullRoot, Path.GetFullPath(candidatePath)) && !string.Equals(fullRoot, Path.GetFullPath(candidatePath), StringComparison.Ordinal))
            throw new InvalidOperationException($"Path '{candidatePath}' escapes release root '{fullRoot}'.");
        EnsureNotReparsePoint(fullRoot, "Release root");
        var current = fullRoot;
        foreach (var segment in Path.GetRelativePath(fullRoot, Path.GetFullPath(candidatePath))
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!TryGetAttributes(current, out var attributes)) return;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Release path '{current}' is a reparse point and cannot be used.");
        }
    }

    private static void EnsureNotReparsePoint(string path, string description)
    {
        if (TryGetAttributes(path, out var attributes) && (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{description} '{path}' is a reparse point and cannot be used.");
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }
}
