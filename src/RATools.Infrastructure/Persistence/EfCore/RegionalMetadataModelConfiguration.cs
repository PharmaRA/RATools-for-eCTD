using Microsoft.EntityFrameworkCore;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class RegionalMetadataRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string ScopeKey { get; set; } = "";
    public string? SequenceNumber { get; set; }
    public long MetadataRevision { get; set; }
    public long? WorkspaceRevision { get; set; }
    public string StandardsSnapshotId { get; set; } = "";
    public Guid? InheritedDefaultsId { get; set; }
    public string? InheritedDefaultsScope { get; set; }
    public string? LegacyDigest { get; set; }
    public string Data { get; set; } = "";
    public string Digest { get; set; } = "";
    public string DataSha256 { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
public sealed class RegionalMetadataHeadRecord
{
    public Guid ApplicationId { get; set; }
    public string ScopeKey { get; set; } = "";
    public Guid MetadataId { get; set; }
}
public sealed class RegionalNodeContextRecord
{
    public Guid MetadataId { get; set; }
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = "";
    public Guid NodeInstanceId { get; set; }
    public int Ordinal { get; set; }
    public string Data { get; set; } = "";
}
public sealed class RegionalDocumentContextRecord
{
    public Guid MetadataId { get; set; }
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = "";
    public Guid PlacementId { get; set; }
    public Guid? NodeInstanceId { get; set; }
    public int Ordinal { get; set; }
    public string Data { get; set; } = "";
}
internal static class RegionalMetadataModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<RegionalMetadataRecord>(entity =>
        {
            entity.ToTable("regional_metadata_revisions", table =>
            {
                table.HasCheckConstraint("CK_regional_metadata_scope", "(\"SequenceNumber\" IS NULL AND \"ScopeKey\" = 'application' AND \"WorkspaceRevision\" IS NULL) OR (\"SequenceNumber\" IS NOT NULL AND \"WorkspaceRevision\" IS NOT NULL AND \"SequenceNumber\" ~ '^[0-9]{4}$' AND \"ScopeKey\" = \"SequenceNumber\" AND \"WorkspaceRevision\" >= 1 AND \"WorkspaceRevision\" < 9007199254740991)");
                table.HasCheckConstraint("CK_regional_metadata_revision", "\"MetadataRevision\" >= 1 AND \"MetadataRevision\" < 9007199254740991");
                table.HasCheckConstraint("CK_regional_metadata_inheritance", "(\"InheritedDefaultsId\" IS NULL AND \"InheritedDefaultsScope\" IS NULL) OR (\"InheritedDefaultsId\" IS NOT NULL AND \"InheritedDefaultsScope\" IS NOT DISTINCT FROM 'application' AND \"SequenceNumber\" IS NOT NULL)");
                table.HasCheckConstraint("CK_regional_metadata_size", "octet_length(\"Data\") <= 8388608");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.ApplicationId, row.ScopeKey, row.Id }).HasName("AK_regional_metadata_scope_id");
            entity.HasIndex(row => new { row.ApplicationId, row.ScopeKey, row.MetadataRevision }).IsUnique().HasDatabaseName("IX_regional_metadata_scope_revision");
            entity.HasIndex(row => new { row.ApplicationId, row.ScopeKey, row.LegacyDigest }).IsUnique()
                .HasFilter("\"LegacyDigest\" IS NOT NULL").HasDatabaseName("IX_regional_metadata_legacy_digest");
            entity.Property(row => row.ScopeKey).HasMaxLength(16).IsRequired();
            entity.Property(row => row.SequenceNumber).HasMaxLength(16);
            entity.Property(row => row.InheritedDefaultsScope).HasMaxLength(16);
            entity.Property(row => row.StandardsSnapshotId).HasMaxLength(71).IsRequired();
            entity.Property(row => row.LegacyDigest).HasMaxLength(64);
            entity.Property(row => row.Data).IsRequired();
            entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.DataSha256).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SequenceRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StandardsSnapshotRecord>().WithMany().HasForeignKey(row => row.StandardsSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RegionalMetadataRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.InheritedDefaultsScope, row.InheritedDefaultsId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.ScopeKey, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RegionalMetadataHeadRecord>(entity =>
        {
            entity.ToTable("regional_metadata_heads"); entity.HasKey(row => new { row.ApplicationId, row.ScopeKey });
            entity.Property(row => row.ScopeKey).HasMaxLength(16);
            entity.HasOne<RegionalMetadataRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.ScopeKey, row.MetadataId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.ScopeKey, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RegionalNodeContextRecord>(entity =>
        {
            entity.ToTable("regional_metadata_node_contexts", table => table.HasCheckConstraint("CK_regional_node_context_ordinal", "\"Ordinal\" >= 0"));
            entity.HasKey(row => new { row.MetadataId, row.NodeInstanceId });
            entity.HasIndex(row => new { row.MetadataId, row.Ordinal }).IsUnique();
            entity.Property(row => row.SequenceNumber).HasMaxLength(16); entity.Property(row => row.Data).IsRequired();
            entity.HasOne<RegionalMetadataRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.MetadataId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.ScopeKey, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SequenceNodeRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.NodeInstanceId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RegionalDocumentContextRecord>(entity =>
        {
            entity.ToTable("regional_metadata_document_contexts", table => table.HasCheckConstraint("CK_regional_document_context_ordinal", "\"Ordinal\" >= 0"));
            entity.HasKey(row => new { row.MetadataId, row.PlacementId });
            entity.HasIndex(row => new { row.MetadataId, row.Ordinal }).IsUnique();
            entity.Property(row => row.SequenceNumber).HasMaxLength(16); entity.Property(row => row.Data).IsRequired();
            entity.HasOne<RegionalMetadataRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.MetadataId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.ScopeKey, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentPlacementRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.PlacementId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.SequenceNumber, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SequenceNodeRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.NodeInstanceId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
