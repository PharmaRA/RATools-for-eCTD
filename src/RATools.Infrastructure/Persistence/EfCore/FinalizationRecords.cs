namespace RATools.Infrastructure.Persistence.EfCore;

// One immutable finalized snapshot. identity digest and every provenance field are
// stored beside the generated id so a replay or corruption is detectable on read.
public sealed class FinalizedSequenceRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = "";
    public long SourceWorkspaceRevision { get; set; }
    public string ManifestDigest { get; set; } = "";
    public string StandardsProfileSnapshotId { get; set; } = "";
    public string IchEctdVersion { get; set; } = "";
    public string? RegionalModule1Version { get; set; }
    public string HistoryBaselineDigest { get; set; } = "";
    public Guid ValidationRunId { get; set; }
    public string ValidationInputManifestDigest { get; set; } = "";
    public string ValidationReportSha256 { get; set; } = "";
    public string ValidationMode { get; set; } = "";
    public bool IsReadyForFinalization { get; set; }
    public string BlockingReasonsJson { get; set; } = "";
    public string CreatedByToolVersion { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string RequestDigest { get; set; } = "";
    public int SchemaVersion { get; set; }
}

// Append-only delivery fact. A corrected channel or reference is a new row.
public sealed class DeliveryRecordRow
{
    public Guid Id { get; set; }
    public Guid FinalizedSequenceId { get; set; }
    public string PackageSha256 { get; set; } = "";
    public string Channel { get; set; } = "";
    public string? ExternalReference { get; set; }
    public string? Note { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public string Digest { get; set; } = "";
    public int SchemaVersion { get; set; }
}

// Append-only receipt. Kind keeps transmission, technical validation and business
// outcome separate; no row merges them.
public sealed class DeliveryReceiptRow
{
    public Guid Id { get; set; }
    public Guid DeliveryRecordId { get; set; }
    public string Kind { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string RawText { get; set; } = "";
    public string RawTextSha256 { get; set; } = "";
    public string? ExternalReference { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public string Digest { get; set; } = "";
    public int SchemaVersion { get; set; }
}

// The explicit continuation of one finalized snapshot. One row per snapshot; it records
// which revision the operator continued from and is never rewritten.
public sealed class FinalizedDraftRevisionRecord
{
    public Guid Id { get; set; }
    public Guid FinalizedSequenceId { get; set; }
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = "";
    public long BaseWorkspaceRevision { get; set; }
    public long WorkspaceRevision { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public DateTime OpenedUtc { get; set; }
    public string Digest { get; set; } = "";
    public int SchemaVersion { get; set; }
}
