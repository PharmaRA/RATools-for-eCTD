using System.Security.Cryptography;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Finalization;

// One file of a planned capture. Exactly one of SourcePath/Content is set: payload and
// standards files are copied from disk while backbones and index-md5.txt are generated.
// Length/Sha256/Md5 are derived by the planner from the content the operator saw; the
// store re-measures the bytes it writes and fails on any disagreement, so a source file
// changed between planning and copying is refused instead of captured silently.
public sealed record ReleaseCaptureFile(string RelativePath, ReleaseFileKind Kind, string? SourcePath, byte[]? Content,
    long Length, string Sha256, string Md5)
{
    public static ReleaseCaptureFile Copy(string relativePath, string sourcePath, long length, string sha256, string md5) =>
        new(relativePath, ReleaseFileKind.Payload, sourcePath, null, length, sha256, md5);

    public static ReleaseCaptureFile Generated(string relativePath, byte[] content,
        ReleaseFileKind kind = ReleaseFileKind.Payload) =>
        new(relativePath, kind, null, content, content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant());
}

public sealed record ReleaseCapturePlan(Guid ApplicationId, string SequenceNumber,
    IReadOnlyList<ReleaseCaptureFile> Files);

// A fully written but not yet published release. The directory is task-owned staging: it
// is moved to its content-addressed release directory only around registration and
// discarded otherwise.
public sealed record ReleaseCaptureStaging(Guid ApplicationId, string SequenceNumber, string Directory,
    IReadOnlyList<ReleaseManifestFile> Files);

public sealed record ReleaseCommitResult(string Directory, bool Created);

// P4-02 storage boundary. Releases are independent copies inside the controlled
// application output root; sources are never hard-linked, so later edits to a workspace
// file cannot change captured bytes.
public interface IReleaseCaptureStore
{
    // Copies/writes every planned file into a fresh staging directory and returns the
    // facts measured from the written bytes, including the derived delivery ZIP.
    Task<ReleaseCaptureStaging> SaveAsync(ReleaseCapturePlan plan, CancellationToken cancellationToken = default);
    // Writes manifest.json, verifies the already measured delivery ZIP, and proves the
    // manifest reproduces its committed digest when read back.
    Task CompleteAsync(ReleaseCaptureStaging staging, ReleaseManifest manifest,
        CancellationToken cancellationToken = default);
    // Moves the completed staging directory to _releases/<sequence>/<manifestDigest>. The
    // name is the content identity: two snapshots of identical validated content share one
    // directory, and a second capture never overwrites proven bytes.
    Task<ReleaseCommitResult> CommitAsync(ReleaseCaptureStaging staging, string manifestDigest,
        CancellationToken cancellationToken = default);
    // Removes only a staging directory this capture created.
    Task DiscardAsync(ReleaseCaptureStaging staging, CancellationToken cancellationToken = default);
    // Removes a release directory this capture created and whose snapshot was not
    // registered. Never called for a directory that already held the same content.
    Task DiscardCommittedAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
        CancellationToken cancellationToken = default);
    // Reads a materialized manifest and proves it is the one the snapshot recorded.
    Task<ReleaseManifest?> ReadManifestAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
        CancellationToken cancellationToken = default);
    // Resolves one captured file of a materialized release. The path must be a member of
    // the verified manifest, so a caller can never address the workspace or another file.
    Task<ReleaseFileContent> OpenFileAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
        string relativePath, CancellationToken cancellationToken = default);
}

// A captured release file that a reader may stream. Path is server-resolved from the
// manifest; Sha256 is the strong validator a reader may cache on.
public sealed record ReleaseFileContent(string Path, string RelativePath, long Length, string Sha256, string MediaType);

public sealed class ReleaseFileNotFoundException(string relativePath)
    : KeyNotFoundException($"The release does not contain '{relativePath}'.");

public interface IFinalizationCaptureProvider
{
    Task<IFinalizationCapture> CaptureAsync(FinalizeSequenceRequest request, CancellationToken cancellationToken = default);

    // Reads the release a committed snapshot recorded and re-verifies it against the
    // snapshot's digest. The store is the authority; a caller cannot ask for another path.
    Task<ReleaseManifest> ReadManifestAsync(FinalizedSequence snapshot, CancellationToken cancellationToken = default);

    // Resolves one captured member of a committed snapshot for preview. History previews
    // read the release, never the current workspace file.
    Task<ReleaseFileContent> OpenFileAsync(FinalizedSequence snapshot, string relativePath,
        CancellationToken cancellationToken = default);
}

// A capture in progress. It owns the staged bytes until the snapshot is registered, so a
// failed finalization cannot leave an unreferenced directory that looks like evidence.
// The archived run facts travel with the capture so the snapshot record and the manifest
// are guaranteed to describe the same run.
public interface IFinalizationCapture
{
    // The digest the finalized snapshot records: derived from the captured bytes and the
    // evidence they were validated against, never from the request.
    string ManifestDigest { get; }

    Guid ValidationRunId { get; }
    string ValidationInputManifestDigest { get; }
    string ReportSha256 { get; }
    PackageValidationMode ValidationMode { get; }
    bool ReportReady { get; }
    IReadOnlyList<string> BlockingReasons { get; }

    // Publishes the captured bytes under their content digest before registration, so a
    // registered snapshot always has its bytes on disk.
    Task PublishAsync(CancellationToken cancellationToken = default);

    // Removes the staging directory. removePublishedDirectory also removes the release
    // directory this capture published; the caller passes true only after proving that no
    // committed snapshot references the digest. Cleanup ignores request cancellation: a
    // cancelled request must not leave staging bytes behind.
    Task DiscardAsync(bool removePublishedDirectory);
}

public sealed class ReleaseCaptureSourceChangedException(string relativePath)
    : IOException($"Captured source '{relativePath}' no longer matches its planned length or digest; the release was not captured.");

public sealed class ReleaseCaptureConflictException(string relativePath)
    : IOException($"Captured release path '{relativePath}' is occupied by different content.");

public sealed class ReleaseCaptureMissingException(string manifestDigest)
    : KeyNotFoundException($"No captured release exists for manifest digest {manifestDigest}.");

public sealed class ReleaseCapturePlanException(string message) : InvalidOperationException(message);
