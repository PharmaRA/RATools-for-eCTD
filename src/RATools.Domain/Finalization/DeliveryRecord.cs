using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.Finalization;

// Delivery records are append-only facts about what an operator actually did with a
// finalized snapshot. They never change the snapshot and never infer submission from a
// completed publish job. A correction is a new record, not an edited one.
public sealed class DeliveryRecord : Entity
{
    public const int CurrentSchemaVersion = 1;

    private DeliveryRecord(Guid id, Guid finalizedSequenceId, string packageSha256, string channel,
        string? externalReference, string? note, string recordedBy, DateTime recordedUtc, int schemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("This delivery record schema is not supported; it cannot be consumed partially.", nameof(schemaVersion));
        if (finalizedSequenceId == Guid.Empty) throw new ArgumentException("A finalized sequence is required.", nameof(finalizedSequenceId));
        ValidationBinding.RequireDigest(packageSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);
        if (recordedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Delivery timestamps must be UTC.", nameof(recordedUtc));
        if (note is { Length: > 2048 }) throw new ArgumentException("A delivery note must be at most 2048 characters.", nameof(note));
        if (externalReference is { Length: > 256 }) throw new ArgumentException("An external reference must be at most 256 characters.", nameof(externalReference));

        Id = id;
        FinalizedSequenceId = finalizedSequenceId;
        PackageSha256 = packageSha256;
        Channel = channel.Trim();
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RecordedBy = recordedBy.Trim();
        RecordedUtc = recordedUtc;
        SchemaVersion = schemaVersion;
        Digest = ComputeDigest();
    }

    public static DeliveryRecord Create(Guid finalizedSequenceId, string packageSha256, string channel,
        string? externalReference, string? note, string recordedBy, DateTime recordedUtc) =>
        new(Guid.NewGuid(), finalizedSequenceId, packageSha256, channel, externalReference, note, recordedBy, recordedUtc,
            CurrentSchemaVersion);

    public static DeliveryRecord Rehydrate(Guid id, Guid finalizedSequenceId, string packageSha256, string channel,
        string? externalReference, string? note, string recordedBy, DateTime recordedUtc, string digest, int schemaVersion)
    {
        var record = new DeliveryRecord(id, finalizedSequenceId, packageSha256, channel, externalReference, note,
            recordedBy, recordedUtc, schemaVersion);
        if (!string.Equals(record.Digest, digest, StringComparison.Ordinal))
            throw new InvalidDataException("The persisted delivery record differs from its committed digest.");
        return record;
    }

    public int SchemaVersion { get; }
    public Guid FinalizedSequenceId { get; }
    // The exact exported package bytes this record refers to. A later re-export with
    // different compression gets a different digest and therefore a different record.
    public string PackageSha256 { get; }
    public string Channel { get; }
    public string? ExternalReference { get; }
    public string? Note { get; }
    public string RecordedBy { get; }
    public DateTime RecordedUtc { get; }
    public string Digest { get; }

    private string ComputeDigest() => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
    {
        schemaVersion = SchemaVersion, finalizedSequenceId = FinalizedSequenceId.ToString("D"),
        packageSha256 = PackageSha256, channel = Channel, externalReference = ExternalReference, note = Note,
        recordedBy = RecordedBy, recordedUtc = RecordedUtc.ToString("O")
    }));
}

public enum DeliveryReceiptKind { Transmission, TechnicalValidation, BusinessOutcome }

// A receipt is evidence, not a state transition. Transport success, technical validation
// and business acceptance stay distinct; none of them is merged into a single boolean.
public sealed class DeliveryReceipt : Entity
{
    public const int CurrentSchemaVersion = 1;

    private DeliveryReceipt(Guid id, Guid deliveryRecordId, DeliveryReceiptKind kind, string outcome,
        string rawText, string? externalReference, string recordedBy, DateTime recordedUtc, int schemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("This delivery receipt schema is not supported; it cannot be consumed partially.", nameof(schemaVersion));
        if (deliveryRecordId == Guid.Empty) throw new ArgumentException("A delivery record is required.", nameof(deliveryRecordId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        if (outcome.Length > 64) throw new ArgumentException("A receipt outcome must be at most 64 characters.", nameof(outcome));
        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);
        if (rawText.Length > 512 * 1024) throw new ArgumentException("A receipt body must be at most 512 KiB.", nameof(rawText));
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);
        if (recordedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Receipt timestamps must be UTC.", nameof(recordedUtc));

        Id = id;
        DeliveryRecordId = deliveryRecordId;
        Kind = kind;
        Outcome = outcome.Trim();
        RawText = rawText;
        RawTextSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawText))).ToLowerInvariant();
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        RecordedBy = recordedBy.Trim();
        RecordedUtc = recordedUtc;
        SchemaVersion = schemaVersion;
        Digest = ComputeDigest();
    }

    public static DeliveryReceipt Create(Guid deliveryRecordId, DeliveryReceiptKind kind, string outcome, string rawText,
        string? externalReference, string recordedBy, DateTime recordedUtc) =>
        new(Guid.NewGuid(), deliveryRecordId, kind, outcome, rawText, externalReference, recordedBy, recordedUtc,
            CurrentSchemaVersion);

    public static DeliveryReceipt Rehydrate(Guid id, Guid deliveryRecordId, DeliveryReceiptKind kind, string outcome,
        string rawText, string rawTextSha256, string? externalReference, string recordedBy, DateTime recordedUtc,
        string digest, int schemaVersion)
    {
        var receipt = new DeliveryReceipt(id, deliveryRecordId, kind, outcome, rawText, externalReference, recordedBy,
            recordedUtc, schemaVersion);
        if (!string.Equals(receipt.RawTextSha256, rawTextSha256, StringComparison.Ordinal) ||
            !string.Equals(receipt.Digest, digest, StringComparison.Ordinal))
            throw new InvalidDataException("The persisted delivery receipt differs from its committed digests.");
        return receipt;
    }

    public int SchemaVersion { get; }
    public Guid DeliveryRecordId { get; }
    public DeliveryReceiptKind Kind { get; }
    public string Outcome { get; }
    public string RawText { get; }
    public string RawTextSha256 { get; }
    public string? ExternalReference { get; }
    public string RecordedBy { get; }
    public DateTime RecordedUtc { get; }
    public string Digest { get; }

    private string ComputeDigest() => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
    {
        schemaVersion = SchemaVersion, deliveryRecordId = DeliveryRecordId.ToString("D"), kind = Kind.ToString(),
        outcome = Outcome, rawTextSha256 = RawTextSha256, externalReference = ExternalReference,
        recordedBy = RecordedBy, recordedUtc = RecordedUtc.ToString("O")
    }));
}
