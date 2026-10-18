using Microsoft.EntityFrameworkCore;

namespace RATools.Infrastructure.Persistence.EfCore;

internal static class FinalizationModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinalizedSequenceRecord>(entity =>
        {
            entity.ToTable("finalized_sequences", table =>
            {
                table.HasCheckConstraint("CK_finalized_sequences_schema", "\"SchemaVersion\" = 1");
                table.HasCheckConstraint("CK_finalized_sequences_revision", "\"SourceWorkspaceRevision\" >= 0 AND \"SourceWorkspaceRevision\" <= 9007199254740991");
                table.HasCheckConstraint("CK_finalized_sequences_ready",
                    "(\"ValidationMode\" <> 'Formal') OR \"IsReadyForFinalization\"");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.SequenceNumber).HasMaxLength(16).IsRequired();
            entity.Property(row => row.ManifestDigest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.StandardsProfileSnapshotId).HasMaxLength(128).IsRequired();
            entity.Property(row => row.IchEctdVersion).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RegionalModule1Version).HasMaxLength(64);
            entity.Property(row => row.HistoryBaselineDigest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ValidationInputManifestDigest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ValidationReportSha256).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ValidationMode).HasMaxLength(32).IsRequired();
            entity.Property(row => row.BlockingReasonsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CreatedByToolVersion).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => row.IdempotencyKey).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.SequenceNumber, row.CreatedUtc });
            // The same captured content may only be finalized once. A new draft revision
            // with different content has a different manifest digest and is allowed.
            entity.HasIndex(row => new { row.ApplicationId, row.SequenceNumber, row.ManifestDigest })
                .IsUnique().HasDatabaseName("IX_finalized_sequences_content");
            // The validation run that authorized a snapshot is evidence; deleting it in
            // another module must not silently detach the snapshot.
            entity.HasOne<RATools.Domain.PackageValidation.PackageValidationRun>().WithMany()
                .HasForeignKey(row => row.ValidationRunId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryRecordRow>(entity =>
        {
            entity.ToTable("finalized_delivery_records", table =>
                table.HasCheckConstraint("CK_delivery_records_schema", "\"SchemaVersion\" = 1"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.PackageSha256).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Channel).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ExternalReference).HasMaxLength(256);
            entity.Property(row => row.Note).HasMaxLength(2048);
            entity.Property(row => row.RecordedBy).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.FinalizedSequenceId, row.RecordedUtc });
            entity.HasOne<FinalizedSequenceRecord>().WithMany().HasForeignKey(row => row.FinalizedSequenceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryReceiptRow>(entity =>
        {
            entity.ToTable("finalized_delivery_receipts", table =>
            {
                table.HasCheckConstraint("CK_delivery_receipts_schema", "\"SchemaVersion\" = 1");
                table.HasCheckConstraint("CK_delivery_receipts_kind",
                    "\"Kind\" IN ('Transmission', 'TechnicalValidation', 'BusinessOutcome')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Kind).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Outcome).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RawTextSha256).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ExternalReference).HasMaxLength(256);
            entity.Property(row => row.RecordedBy).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.DeliveryRecordId, row.RecordedUtc });
            entity.HasOne<DeliveryRecordRow>().WithMany().HasForeignKey(row => row.DeliveryRecordId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
