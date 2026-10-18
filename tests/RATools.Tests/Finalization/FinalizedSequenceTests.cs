using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.Finalization;

public sealed class FinalizedSequenceTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static FinalizedSequence Create(PackageValidationMode mode = PackageValidationMode.Development,
        bool ready = false, string? key = null, string manifest = "manifest-a") =>
        FinalizedSequence.Create(Guid.NewGuid(), "0001", 4, Digest(manifest),
            PackageValidationCatalog.UsProfile, "3.2.2", "3.3", new string('b', 64), Guid.NewGuid(), Digest("input"),
            Digest("report"), mode, ready, ready ? [] : ["ProfileNotQualified: test"], "ratools-finalization-v1",
            key ?? Guid.NewGuid().ToString("N"), Now);

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    [Fact]
    public void IdentityDigestCoversEveryProvenanceFieldAndIsDeterministic()
    {
        var key = Guid.NewGuid().ToString("N");
        var first = Create(key: key);
        var rehydrated = FinalizedSequence.Rehydrate(first.Id, first.ApplicationId, first.SequenceNumber,
            first.SourceWorkspaceRevision, first.ManifestDigest, first.StandardsProfileSnapshotId, first.IchEctdVersion,
            first.RegionalModule1Version, first.HistoryBaselineDigest, first.ValidationRunId,
            first.ValidationInputManifestDigest, first.ValidationReportSha256, first.ValidationMode,
            first.IsReadyForFinalization, first.BlockingReasons, first.CreatedByToolVersion, first.IdempotencyKey,
            first.CreatedUtc, first.RequestDigest, first.SchemaVersion);
        Assert.Equal(first.RequestDigest, rehydrated.RequestDigest);
        // Changing the manifest digest changes the identity, so a retry can never reuse a
        // snapshot for different validated content.
        Assert.NotEqual(first.RequestDigest, Create(key: key, manifest: "manifest-b").RequestDigest);
        // A tampered persisted digest is rejected on read.
        Assert.Throws<InvalidDataException>(() => FinalizedSequence.Rehydrate(first.Id, first.ApplicationId,
            first.SequenceNumber, first.SourceWorkspaceRevision, first.ManifestDigest, first.StandardsProfileSnapshotId,
            first.IchEctdVersion, first.RegionalModule1Version, first.HistoryBaselineDigest, first.ValidationRunId,
            first.ValidationInputManifestDigest, first.ValidationReportSha256, first.ValidationMode,
            first.IsReadyForFinalization, first.BlockingReasons, first.CreatedByToolVersion, first.IdempotencyKey,
            first.CreatedUtc, new string('f', 64), first.SchemaVersion));
    }

    [Fact]
    public void FormalSnapshotsRequireRecordedReadinessAndUnknownSchemasAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Create(PackageValidationMode.Formal, ready: false));
        var formal = Create(PackageValidationMode.Formal, ready: true);
        Assert.True(formal.IsReadyForFinalization);
        var development = Create();
        // Development snapshots may archive blocking reasons but cannot claim readiness.
        Assert.False(development.IsReadyForFinalization);
        Assert.Single(development.BlockingReasons);
        Assert.Throws<ArgumentException>(() => FinalizedSequence.Rehydrate(development.Id, development.ApplicationId,
            development.SequenceNumber, development.SourceWorkspaceRevision, development.ManifestDigest,
            development.StandardsProfileSnapshotId, development.IchEctdVersion, development.RegionalModule1Version,
            development.HistoryBaselineDigest, development.ValidationRunId, development.ValidationInputManifestDigest,
            development.ValidationReportSha256, development.ValidationMode, development.IsReadyForFinalization,
            development.BlockingReasons, development.CreatedByToolVersion, development.IdempotencyKey,
            development.CreatedUtc, development.RequestDigest, schemaVersion: 2));
    }

    [Fact]
    public void DeliveryRecordsAreImmutableFactsAndReceiptsKeepOutcomeKindsSeparate()
    {
        var snapshot = Create(PackageValidationMode.Formal, ready: true);
        var record = DeliveryRecord.Create(snapshot.Id, Digest("package"), "eCTD gateway", "EXT-42", "Operator export.",
            "operator", Now);
        var replay = DeliveryRecord.Rehydrate(record.Id, record.FinalizedSequenceId, record.PackageSha256, record.Channel,
            record.ExternalReference, record.Note, record.RecordedBy, record.RecordedUtc, record.Digest, record.SchemaVersion);
        Assert.Equal(record.Digest, replay.Digest);
        Assert.NotEqual(record.Digest, DeliveryRecord.Create(snapshot.Id, Digest("other"), "eCTD gateway", "EXT-42",
            "Operator export.", "operator", Now).Digest);
        Assert.Throws<InvalidDataException>(() => DeliveryRecord.Rehydrate(record.Id, record.FinalizedSequenceId,
            record.PackageSha256, record.Channel, record.ExternalReference, record.Note, record.RecordedBy,
            record.RecordedUtc, new string('f', 64), record.SchemaVersion));

        var transmission = DeliveryReceipt.Create(record.Id, DeliveryReceiptKind.Transmission, "Accepted", "<ack />",
            "EXT-42", "operator", Now);
        var business = DeliveryReceipt.Create(record.Id, DeliveryReceiptKind.BusinessOutcome, "Pending", "<pending />",
            "EXT-42", "operator", Now);
        Assert.NotEqual(transmission.Kind, business.Kind);
        Assert.NotEqual(transmission.Digest, business.Digest);
        Assert.Throws<ArgumentException>(() => DeliveryReceipt.Create(record.Id, DeliveryReceiptKind.Transmission,
            "Accepted", " ", null, "operator", Now));
        Assert.Throws<ArgumentException>(() => DeliveryReceipt.Create(record.Id, DeliveryReceiptKind.Transmission,
            "Accepted", "<ack />", null, "operator", DateTime.Now));
    }

    [Fact]
    public void TimestampDigestsSurviveTheDatabaseMicrosecondPrecision()
    {
        // PostgreSQL timestamptz keeps microseconds only; the committed digest must be
        // reproducible from the reloaded value, while the returned row keeps the stored
        // (truncated) timestamp.
        var subMicrosecond = Now.AddTicks(7);
        var snapshot = Create(PackageValidationMode.Formal, ready: true);
        var record = DeliveryRecord.Create(snapshot.Id, Digest("package"), "gateway", null, null, "operator", subMicrosecond);
        Assert.Equal(0, record.RecordedUtc.Ticks % 10);
        var replay = DeliveryRecord.Rehydrate(record.Id, record.FinalizedSequenceId, record.PackageSha256, record.Channel,
            record.ExternalReference, record.Note, record.RecordedBy, record.RecordedUtc, record.Digest, record.SchemaVersion);
        Assert.Equal(record.Digest, replay.Digest);
        var receipt = DeliveryReceipt.Create(record.Id, DeliveryReceiptKind.Transmission, "Accepted", "<ack />",
            null, "operator", subMicrosecond);
        Assert.Equal(0, receipt.RecordedUtc.Ticks % 10);
        Assert.Equal(receipt.Digest, DeliveryReceipt.Rehydrate(receipt.Id, receipt.DeliveryRecordId, receipt.Kind,
            receipt.Outcome, receipt.RawText, receipt.RawTextSha256, receipt.ExternalReference, receipt.RecordedBy,
            receipt.RecordedUtc, receipt.Digest, receipt.SchemaVersion).Digest);
    }

    [Fact]
    public void OverlongEvidenceFieldsAreRejectedBeforeTheDatabase()
    {
        var snapshot = Create(PackageValidationMode.Formal, ready: true);
        Assert.Throws<ArgumentException>(() => DeliveryRecord.Create(snapshot.Id, Digest("package"),
            new string('c', 65), null, null, "operator", Now));
        Assert.Throws<ArgumentException>(() => DeliveryRecord.Create(snapshot.Id, Digest("package"),
            "gateway", null, null, new string('r', 129), Now));
        Assert.Throws<ArgumentException>(() => DeliveryRecord.Create(snapshot.Id, Digest("package"),
            "gateway", new string('e', 257), null, "operator", Now));
        Assert.Throws<ArgumentException>(() => DeliveryReceipt.Create(snapshot.Id, DeliveryReceiptKind.Transmission,
            "Accepted", "<ack />", null, new string('r', 129), Now));
        Assert.Throws<ArgumentException>(() => FinalizedSequence.Create(Guid.NewGuid(), "0001", 1, Digest("manifest"),
            PackageValidationCatalog.UsProfile, new string('v', 65), new string('m', 65), new string('h', 64),
            Guid.NewGuid(), Digest("input"), Digest("report"), PackageValidationMode.Development, false, [],
            "ratools-finalization-v1", Guid.NewGuid().ToString("N"), Now));
    }
}
