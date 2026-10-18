using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.Finalization;

// The immutable identity of one finalized sequence version. A snapshot names exactly the
// content digest, standards snapshot, history baseline and completed validation report it
// was created from; later draft changes never rewrite it. Content capture/staging and
// delivery records are separate facts (P4-02..06); this type fixes the identity and its
// provenance so a retry can never silently produce a different snapshot.
public sealed class FinalizedSequence : Entity
{
    public const int CurrentSchemaVersion = 1;
    private const long LargestRevision = 9007199254740991;
    private const int MaxBlockingReasons = 200;

    private FinalizedSequence(Guid id, Guid applicationId, string sequenceNumber, long sourceWorkspaceRevision,
        string manifestDigest, string standardsProfileSnapshotId, string ichEctdVersion, string? regionalModule1Version,
        string historyBaselineDigest, Guid validationRunId, string validationInputManifestDigest,
        string validationReportSha256, PackageValidationMode validationMode, bool isReadyForFinalization,
        IEnumerable<string> blockingReasons, string createdByToolVersion, string idempotencyKey, DateTime createdUtc,
        int schemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("This finalization snapshot schema is not supported; it cannot be consumed partially.", nameof(schemaVersion));
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        PackageInputManifest.RequireSequenceNumber(sequenceNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceWorkspaceRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceWorkspaceRevision, LargestRevision);
        ValidationBinding.RequireDigest(manifestDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(standardsProfileSnapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ichEctdVersion);
        ValidationBinding.RequireDigest(historyBaselineDigest);
        if (validationRunId == Guid.Empty) throw new ArgumentException("A completed validation run reference is required.", nameof(validationRunId));
        ValidationBinding.RequireDigest(validationInputManifestDigest);
        ValidationBinding.RequireDigest(validationReportSha256);
        if (!Enum.IsDefined(validationMode)) throw new ArgumentOutOfRangeException(nameof(validationMode));
        // Formal finalization is the only scope that may later carry delivery records;
        // its readiness must be established by the validation report, not by this type.
        if (validationMode == PackageValidationMode.Formal && !isReadyForFinalization)
            throw new ArgumentException("A formal snapshot requires a report that is ready for finalization.", nameof(isReadyForFinalization));
        var reasons = blockingReasons.ToArray();
        if (reasons.Length > MaxBlockingReasons || reasons.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Blocking reasons must be bounded, nonblank entries.", nameof(blockingReasons));
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByToolVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200 || idempotencyKey != idempotencyKey.Trim())
            throw new ArgumentException("Use an exact idempotency key of at most 200 characters without surrounding whitespace.", nameof(idempotencyKey));
        if (createdUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Finalization timestamps must be UTC.", nameof(createdUtc));

        Id = id;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber;
        SourceWorkspaceRevision = sourceWorkspaceRevision;
        ManifestDigest = manifestDigest;
        StandardsProfileSnapshotId = standardsProfileSnapshotId.Trim();
        IchEctdVersion = ichEctdVersion.Trim();
        RegionalModule1Version = string.IsNullOrWhiteSpace(regionalModule1Version) ? null : regionalModule1Version.Trim();
        HistoryBaselineDigest = historyBaselineDigest;
        ValidationRunId = validationRunId;
        ValidationInputManifestDigest = validationInputManifestDigest;
        ValidationReportSha256 = validationReportSha256;
        ValidationMode = validationMode;
        IsReadyForFinalization = isReadyForFinalization;
        BlockingReasons = Array.AsReadOnly(reasons);
        CreatedByToolVersion = createdByToolVersion.Trim();
        IdempotencyKey = idempotencyKey;
        CreatedUtc = createdUtc;
        SchemaVersion = schemaVersion;
        RequestDigest = ComputeRequestDigest();
    }

    public static FinalizedSequence Create(Guid applicationId, string sequenceNumber, long sourceWorkspaceRevision,
        string manifestDigest, string standardsProfileSnapshotId, string ichEctdVersion, string? regionalModule1Version,
        string historyBaselineDigest, Guid validationRunId, string validationInputManifestDigest,
        string validationReportSha256, PackageValidationMode validationMode, bool isReadyForFinalization,
        IEnumerable<string> blockingReasons, string createdByToolVersion, string idempotencyKey, DateTime createdUtc) =>
        new(Guid.NewGuid(), applicationId, sequenceNumber, sourceWorkspaceRevision, manifestDigest,
            standardsProfileSnapshotId, ichEctdVersion, regionalModule1Version, historyBaselineDigest, validationRunId,
            validationInputManifestDigest, validationReportSha256, validationMode, isReadyForFinalization,
            blockingReasons, createdByToolVersion, idempotencyKey, createdUtc, CurrentSchemaVersion);

    // The id is rehydrated as stored; every content and provenance field is revalidated.
    public static FinalizedSequence Rehydrate(Guid id, Guid applicationId, string sequenceNumber, long sourceWorkspaceRevision,
        string manifestDigest, string standardsProfileSnapshotId, string ichEctdVersion, string? regionalModule1Version,
        string historyBaselineDigest, Guid validationRunId, string validationInputManifestDigest,
        string validationReportSha256, PackageValidationMode validationMode, bool isReadyForFinalization,
        IEnumerable<string> blockingReasons, string createdByToolVersion, string idempotencyKey, DateTime createdUtc,
        string requestDigest, int schemaVersion)
    {
        var sequence = new FinalizedSequence(id, applicationId, sequenceNumber, sourceWorkspaceRevision, manifestDigest,
            standardsProfileSnapshotId, ichEctdVersion, regionalModule1Version, historyBaselineDigest, validationRunId,
            validationInputManifestDigest, validationReportSha256, validationMode, isReadyForFinalization,
            blockingReasons, createdByToolVersion, idempotencyKey, createdUtc, schemaVersion);
        if (!string.Equals(sequence.RequestDigest, requestDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The persisted finalization snapshot differs from its committed identity digest.");
        return sequence;
    }

    public int SchemaVersion { get; }
    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    // The workspace revision the snapshot was requested for. It is evidence of what the
    // operator saw, not a mutable pointer: later revisions do not change this snapshot.
    public long SourceWorkspaceRevision { get; }
    // Digest of the captured content manifest (P4-03). It identifies the exact delivered
    // bytes and metadata this snapshot was built from.
    public string ManifestDigest { get; }
    public string StandardsProfileSnapshotId { get; }
    public string IchEctdVersion { get; }
    public string? RegionalModule1Version { get; }
    public string HistoryBaselineDigest { get; }
    public Guid ValidationRunId { get; }
    public string ValidationInputManifestDigest { get; }
    public string ValidationReportSha256 { get; }
    public PackageValidationMode ValidationMode { get; }
    // Readiness as recorded by the bound report. A development snapshot may be complete
    // but not ready; only a formal, ready snapshot can carry delivery records.
    public bool IsReadyForFinalization { get; }
    public IReadOnlyList<string> BlockingReasons { get; }
    public string CreatedByToolVersion { get; }
    public string IdempotencyKey { get; }
    public DateTime CreatedUtc { get; }
    // Canonical identity of every claim except the generated id and wall-clock time.
    // A retry with the same key must match this digest exactly.
    public string RequestDigest { get; }

    private string ComputeRequestDigest() => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
    {
        schemaVersion = SchemaVersion, applicationId = ApplicationId.ToString("D"), sequenceNumber = SequenceNumber,
        sourceWorkspaceRevision = SourceWorkspaceRevision, manifestDigest = ManifestDigest,
        standardsProfileSnapshotId = StandardsProfileSnapshotId, ichEctdVersion = IchEctdVersion,
        regionalModule1Version = RegionalModule1Version, historyBaselineDigest = HistoryBaselineDigest,
        validationRunId = ValidationRunId.ToString("D"), validationInputManifestDigest = ValidationInputManifestDigest,
        validationReportSha256 = ValidationReportSha256, validationMode = ValidationMode.ToString(),
        isReadyForFinalization = IsReadyForFinalization, blockingReasons = BlockingReasons,
        createdByToolVersion = CreatedByToolVersion, idempotencyKey = IdempotencyKey
    }));
}
