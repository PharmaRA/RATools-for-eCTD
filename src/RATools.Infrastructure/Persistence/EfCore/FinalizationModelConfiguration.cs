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
            // The same captured content may only be finalized once per validation mode.
            // A development snapshot is not delivery authorization, so it must not block a
            // later formal finalization of the same bytes; a formal snapshot is unique.
            entity.HasIndex(row => new { row.ApplicationId, row.SequenceNumber, row.ManifestDigest, row.ValidationMode })
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

        modelBuilder.Entity<FinalizedDraftRevisionRecord>(entity =>
        {
            entity.ToTable("finalized_draft_revisions", table =>
            {
                table.HasCheckConstraint("CK_finalized_draft_schema", "\"SchemaVersion\" = 1");
                table.HasCheckConstraint("CK_finalized_draft_revision",
                    "\"BaseWorkspaceRevision\" >= 0 AND \"WorkspaceRevision\" > \"BaseWorkspaceRevision\"");
            });
            entity.HasKey(row => row.Id);
            // One continuation per snapshot: two drafts would split the editable content
            // of one delivered version.
            entity.HasIndex(row => row.FinalizedSequenceId).IsUnique().HasDatabaseName("IX_finalized_draft_revisions_Snapshot");
            entity.HasIndex(row => row.IdempotencyKey).IsUnique().HasDatabaseName("IX_finalized_draft_revisions_IdempotencyKey");
            entity.HasIndex(row => new { row.ApplicationId, row.SequenceNumber, row.OpenedUtc });
            entity.Property(row => row.SequenceNumber).HasMaxLength(16).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
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
