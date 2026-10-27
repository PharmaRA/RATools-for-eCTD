using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.Finalization;

// Explicit continuation of a finalized sequence. A finalized snapshot freezes the
// workspace; editing resumes only through a recorded draft that names the snapshot it
// continues from. The record is the audit answer to "which revision continues which
// delivered version" and is never rewritten.
public sealed class FinalizedDraftRevision : Entity
{
    public const int CurrentSchemaVersion = 1;

    private FinalizedDraftRevision(Guid id, Guid finalizedSequenceId, Guid applicationId, string sequenceNumber,
        long baseWorkspaceRevision, long workspaceRevision, string idempotencyKey, DateTime openedUtc, int schemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("This finalized draft schema is not supported; it cannot be consumed partially.",
                nameof(schemaVersion));
        if (finalizedSequenceId == Guid.Empty)
            throw new ArgumentException("A finalized sequence reference is required.", nameof(finalizedSequenceId));
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        PackageInputManifest.RequireSequenceNumber(sequenceNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(baseWorkspaceRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(baseWorkspaceRevision, 9007199254740991L);
        // The snapshot pinned its source revision; an opened draft must have moved the
        // workspace past it. A legacy sequence edited before this boundary existed records
        // the revision it already had when continuation was made explicit.
        if (workspaceRevision <= baseWorkspaceRevision)
            throw new ArgumentException("A new draft must advance the workspace revision past the finalized revision.",
                nameof(workspaceRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200 || idempotencyKey != idempotencyKey.Trim())
            throw new ArgumentException("Use an exact idempotency key of at most 200 characters without surrounding whitespace.",
                nameof(idempotencyKey));
        if (openedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Draft timestamps must be UTC.", nameof(openedUtc));

        Id = id;
        FinalizedSequenceId = finalizedSequenceId;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber;
        BaseWorkspaceRevision = baseWorkspaceRevision;
        WorkspaceRevision = workspaceRevision;
        IdempotencyKey = idempotencyKey;
        // Truncate before hashing so a PostgreSQL microsecond round trip reproduces the
        // committed digest exactly.
        OpenedUtc = FinalizationFields.TruncateToMicroseconds(openedUtc);
        SchemaVersion = schemaVersion;
        Digest = ComputeDigest();
    }

    public static FinalizedDraftRevision Create(Guid finalizedSequenceId, Guid applicationId, string sequenceNumber,
        long baseWorkspaceRevision, long workspaceRevision, string idempotencyKey, DateTime openedUtc) =>
        new(Guid.NewGuid(), finalizedSequenceId, applicationId, sequenceNumber, baseWorkspaceRevision, workspaceRevision,
            idempotencyKey, openedUtc, CurrentSchemaVersion);

    // The id is rehydrated as stored; every claim is revalidated so a corrupt row cannot
    // silently authorize editing under a different snapshot or revision.
    public static FinalizedDraftRevision Rehydrate(Guid id, Guid finalizedSequenceId, Guid applicationId,
        string sequenceNumber, long baseWorkspaceRevision, long workspaceRevision, string idempotencyKey,
        DateTime openedUtc, string digest, int schemaVersion)
    {
        var draft = new FinalizedDraftRevision(id, finalizedSequenceId, applicationId, sequenceNumber,
            baseWorkspaceRevision, workspaceRevision, idempotencyKey, openedUtc, schemaVersion);
        if (!string.Equals(draft.Digest, digest, StringComparison.Ordinal))
            throw new InvalidDataException("The persisted finalized draft differs from its committed digest.");
        return draft;
    }

    public int SchemaVersion { get; }
    public Guid FinalizedSequenceId { get; }
    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    // The snapshot's pinned revision this draft continues from.
    public long BaseWorkspaceRevision { get; }
    // The workspace revision the sequence carried when the draft opened. Later edits
    // advance the sequence revision; this row stays as the continuation evidence.
    public long WorkspaceRevision { get; }
    public string IdempotencyKey { get; }
    public DateTime OpenedUtc { get; }
    public string Digest { get; }

    private string ComputeDigest() => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
    {
        schemaVersion = SchemaVersion, finalizedSequenceId = FinalizedSequenceId.ToString("D"),
        applicationId = ApplicationId.ToString("D"), sequenceNumber = SequenceNumber,
        baseWorkspaceRevision = BaseWorkspaceRevision, workspaceRevision = WorkspaceRevision,
        idempotencyKey = IdempotencyKey, openedUtc = OpenedUtc.ToString("O")
    }));
}

// Thrown when the latest finalized content has no open draft: every workspace write must
// be refused until the operator explicitly continues in a new draft. It deliberately does
// not derive from InvalidOperationException, whose broad controller handlers would map it
// to an ambiguous conflict without the machine-readable code.
public sealed class WorkspaceFinalizedException(Guid applicationId, string sequenceNumber, Guid finalizedSequenceId,
    long sourceWorkspaceRevision)
    : Exception($"Sequence {sequenceNumber} content was finalized as snapshot {finalizedSequenceId} " +
        $"at revision {sourceWorkspaceRevision}. Create a new draft revision before editing.")
{
    public Guid ApplicationId { get; } = applicationId;
    public string SequenceNumber { get; } = sequenceNumber;
    public Guid FinalizedSequenceId { get; } = finalizedSequenceId;
    public long SourceWorkspaceRevision { get; } = sourceWorkspaceRevision;
}

// A newer snapshot exists: a draft opened from the older one would make the editable
// content ambiguous about which delivered version it continues.
public sealed class FinalizationDraftRequiresLatestException(Guid finalizedSequenceId, Guid latestFinalizedSequenceId)
    : InvalidOperationException($"Finalized sequence {finalizedSequenceId} is superseded by snapshot " +
        $"{latestFinalizedSequenceId}; open the draft from the latest snapshot.")
{
    public Guid FinalizedSequenceId { get; } = finalizedSequenceId;
    public Guid LatestFinalizedSequenceId { get; } = latestFinalizedSequenceId;
}

// The snapshot already continues in a draft. A second draft would split the editable
// content into two continuations of one delivered version. The revision is unknown when
// the conflict was detected from a database constraint under an aborted transaction.
public sealed class FinalizationDraftAlreadyOpenException(Guid finalizedSequenceId, long? workspaceRevision)
    : InvalidOperationException($"Finalized sequence {finalizedSequenceId} already continues in a draft" +
        (workspaceRevision is { } revision ? $" at workspace revision {revision}." : "."))
{
    public Guid FinalizedSequenceId { get; } = finalizedSequenceId;
    public long? WorkspaceRevision { get; } = workspaceRevision;
}
