using System.Text.Json;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.Persistence.EfCore;

internal static class FinalizationRecordMapping
{
    public static FinalizedSequenceRecord ToRecord(this FinalizedSequence sequence) => new()
    {
        Id = sequence.Id,
        ApplicationId = sequence.ApplicationId,
        SequenceNumber = sequence.SequenceNumber,
        SourceWorkspaceRevision = sequence.SourceWorkspaceRevision,
        ManifestDigest = sequence.ManifestDigest,
        StandardsProfileSnapshotId = sequence.StandardsProfileSnapshotId,
        IchEctdVersion = sequence.IchEctdVersion,
        RegionalModule1Version = sequence.RegionalModule1Version,
        HistoryBaselineDigest = sequence.HistoryBaselineDigest,
        ValidationRunId = sequence.ValidationRunId,
        ValidationInputManifestDigest = sequence.ValidationInputManifestDigest,
        ValidationReportSha256 = sequence.ValidationReportSha256,
        ValidationMode = sequence.ValidationMode.ToString(),
        IsReadyForFinalization = sequence.IsReadyForFinalization,
        BlockingReasonsJson = JsonSerializer.Serialize(sequence.BlockingReasons),
        CreatedByToolVersion = sequence.CreatedByToolVersion,
        IdempotencyKey = sequence.IdempotencyKey,
        CreatedUtc = sequence.CreatedUtc,
        RequestDigest = sequence.RequestDigest,
        SchemaVersion = sequence.SchemaVersion
    };

    public static FinalizedSequence ToDomain(this FinalizedSequenceRecord row)
    {
        var reasons = JsonSerializer.Deserialize<string[]>(row.BlockingReasonsJson)
            ?? throw new InvalidDataException("Stored finalization blocking reasons are not a string array.");
        return FinalizedSequence.Rehydrate(row.Id, row.ApplicationId, row.SequenceNumber, row.SourceWorkspaceRevision,
            row.ManifestDigest, row.StandardsProfileSnapshotId, row.IchEctdVersion, row.RegionalModule1Version,
            row.HistoryBaselineDigest, row.ValidationRunId, row.ValidationInputManifestDigest,
            row.ValidationReportSha256, Enum.Parse<PackageValidationMode>(row.ValidationMode),
            row.IsReadyForFinalization, reasons, row.CreatedByToolVersion, row.IdempotencyKey, row.CreatedUtc,
            row.RequestDigest, row.SchemaVersion);
    }

    public static DeliveryRecordRow ToRecord(this DeliveryRecord record) => new()
    {
        Id = record.Id,
        FinalizedSequenceId = record.FinalizedSequenceId,
        PackageSha256 = record.PackageSha256,
        Channel = record.Channel,
        ExternalReference = record.ExternalReference,
        Note = record.Note,
        RecordedBy = record.RecordedBy,
        RecordedUtc = record.RecordedUtc,
        Digest = record.Digest,
        SchemaVersion = record.SchemaVersion
    };

    public static DeliveryRecord ToDomain(this DeliveryRecordRow row) => DeliveryRecord.Rehydrate(row.Id,
        row.FinalizedSequenceId, row.PackageSha256, row.Channel, row.ExternalReference, row.Note, row.RecordedBy,
        row.RecordedUtc, row.Digest, row.SchemaVersion);

    public static DeliveryReceiptRow ToRecord(this DeliveryReceipt receipt) => new()
    {
        Id = receipt.Id,
        DeliveryRecordId = receipt.DeliveryRecordId,
        Kind = receipt.Kind.ToString(),
        Outcome = receipt.Outcome,
        RawText = receipt.RawText,
        RawTextSha256 = receipt.RawTextSha256,
        ExternalReference = receipt.ExternalReference,
        RecordedBy = receipt.RecordedBy,
        RecordedUtc = receipt.RecordedUtc,
        Digest = receipt.Digest,
        SchemaVersion = receipt.SchemaVersion
    };

    public static DeliveryReceipt ToDomain(this DeliveryReceiptRow row) => DeliveryReceipt.Rehydrate(row.Id,
        row.DeliveryRecordId, Enum.Parse<DeliveryReceiptKind>(row.Kind), row.Outcome, row.RawText, row.RawTextSha256,
        row.ExternalReference, row.RecordedBy, row.RecordedUtc, row.Digest, row.SchemaVersion);

    public static FinalizedDraftRevisionRecord ToRecord(this FinalizedDraftRevision draft) => new()
    {
        Id = draft.Id,
        FinalizedSequenceId = draft.FinalizedSequenceId,
        ApplicationId = draft.ApplicationId,
        SequenceNumber = draft.SequenceNumber,
        BaseWorkspaceRevision = draft.BaseWorkspaceRevision,
        WorkspaceRevision = draft.WorkspaceRevision,
        IdempotencyKey = draft.IdempotencyKey,
        OpenedUtc = draft.OpenedUtc,
        Digest = draft.Digest,
        SchemaVersion = draft.SchemaVersion
    };

    public static FinalizedDraftRevision ToDomain(this FinalizedDraftRevisionRecord row) =>
        FinalizedDraftRevision.Rehydrate(row.Id, row.FinalizedSequenceId, row.ApplicationId, row.SequenceNumber,
            row.BaseWorkspaceRevision, row.WorkspaceRevision, row.IdempotencyKey, row.OpenedUtc, row.Digest,
            row.SchemaVersion);
}
