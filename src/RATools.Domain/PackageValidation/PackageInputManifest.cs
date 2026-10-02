using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Domain.PackageValidation;

public sealed record PackageInputFile(string LogicalPath, long Length, string Sha256, string Md5);
public sealed record PackageInputDirectory(string LogicalPath, bool IsEmpty);
public sealed record PackageSequenceCandidate(string SequenceNumber, string RootRelativePath);
public enum PackageSourceKind { Directory, Zip }
public enum HistorySourceKind { ImportedExternal, LegacyWorkspace, Finalized }
public enum HistoryTrustStatus { Unverified, LegacyUnverified, VerifiedForScope, Rejected }

public sealed class PackageInputManifest
{
    public PackageInputManifest(Guid applicationId, string sequenceNumber, IEnumerable<PackageInputFile> files,
        IEnumerable<PackageInputDirectory> directories, IEnumerable<PackageInputFile> additionalFiles,
        IEnumerable<PackageInputDirectory> additionalDirectories)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("Select an explicit application identity.", nameof(applicationId));
        RequireSequenceNumber(sequenceNumber);
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber;
        Files = Array.AsReadOnly(files.OrderBy(file => file.LogicalPath, StringComparer.Ordinal).ToArray());
        Directories = Array.AsReadOnly(directories.OrderBy(directory => directory.LogicalPath, StringComparer.Ordinal).ToArray());
        AdditionalFiles = Array.AsReadOnly(additionalFiles.OrderBy(file => file.LogicalPath, StringComparer.Ordinal).ToArray());
        AdditionalDirectories = Array.AsReadOnly(additionalDirectories.OrderBy(directory => directory.LogicalPath, StringComparer.Ordinal).ToArray());
        // v2 adds empty-directory and surrounding-input inventory to the G0 v1 content contract.
        Digest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2, applicationId = applicationId.ToString("D"), sequenceNumber,
            files = Files.Select(WireFile), directories = Directories.Select(WireDirectory),
            additionalFiles = AdditionalFiles.Select(WireFile), additionalDirectories = AdditionalDirectories.Select(WireDirectory)
        }));
    }

    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    public IReadOnlyList<PackageInputFile> Files { get; }
    public IReadOnlyList<PackageInputDirectory> Directories { get; }
    public IReadOnlyList<PackageInputFile> AdditionalFiles { get; }
    public IReadOnlyList<PackageInputDirectory> AdditionalDirectories { get; }
    public string Digest { get; }

    private static object WireFile(PackageInputFile file) => new { logicalPath = file.LogicalPath, length = file.Length, sha256 = file.Sha256, md5 = file.Md5 };
    private static object WireDirectory(PackageInputDirectory directory) => new { logicalPath = directory.LogicalPath, isEmpty = directory.IsEmpty };

    public static void RequireSequenceNumber(string value)
    {
        if (value is null || value.Length != 4 || value.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("Sequence numbers must contain exactly four ASCII digits.", nameof(value));
    }
}

public sealed record HistoryBaselineEntry(string SequenceNumber, HistorySourceKind SourceKind, string SourceId,
    string ContentDigest, string ProfileSnapshotId, HistoryTrustStatus TrustStatus,
    Guid? ValidationRunId = null, Guid? FinalizedSequenceId = null);

public sealed class HistoryBaselineManifest
{
    public HistoryBaselineManifest(Guid applicationId, string targetSequence, IEnumerable<HistoryBaselineEntry> entries)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("Select an application.", nameof(applicationId));
        PackageInputManifest.RequireSequenceNumber(targetSequence);
        ApplicationId = applicationId;
        TargetSequence = targetSequence;
        var copy = entries.OrderBy(entry => entry.SequenceNumber, StringComparer.Ordinal).ToArray();
        if (copy.Select(entry => entry.SequenceNumber).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Select exactly one source version for each historical sequence.", nameof(entries));
        foreach (var entry in copy)
        {
            PackageInputManifest.RequireSequenceNumber(entry.SequenceNumber);
            if (string.CompareOrdinal(entry.SequenceNumber, targetSequence) >= 0)
                throw new ArgumentException("Historical baselines only contain earlier sequences; same-sequence references use the target input.", nameof(entries));
            ValidationBinding.RequireDigest(entry.ContentDigest);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.SourceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.ProfileSnapshotId);
            if (!Enum.IsDefined(entry.SourceKind) || !Enum.IsDefined(entry.TrustStatus))
                throw new ArgumentException("Unknown history provenance or trust status.", nameof(entries));
            if (entry.TrustStatus == HistoryTrustStatus.VerifiedForScope && entry.ValidationRunId is null)
                throw new ArgumentException("Verified history requires a validation run reference.", nameof(entries));
            if (entry.SourceKind == HistorySourceKind.Finalized && entry.FinalizedSequenceId is null)
                throw new ArgumentException("Finalized history requires its immutable snapshot reference.", nameof(entries));
        }
        Entries = Array.AsReadOnly(copy);
        Digest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, applicationId = applicationId.ToString("D"),
            entries = copy.Select(entry => new
            {
                sequenceNumber = entry.SequenceNumber, sourceKind = entry.SourceKind.ToString(), sourceId = entry.SourceId,
                contentDigest = entry.ContentDigest, profileSnapshotId = entry.ProfileSnapshotId, trustStatus = entry.TrustStatus.ToString(),
                validationRunId = entry.ValidationRunId?.ToString("D"), finalizedSequenceId = entry.FinalizedSequenceId?.ToString("D")
            })
        }));
    }

    public Guid ApplicationId { get; }
    public string TargetSequence { get; }
    public IReadOnlyList<HistoryBaselineEntry> Entries { get; }
    public string Digest { get; }
}
