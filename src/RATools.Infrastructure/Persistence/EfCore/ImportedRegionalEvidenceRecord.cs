using Microsoft.EntityFrameworkCore;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class ImportedRegionalEvidenceRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = "";
    public Guid MetadataId { get; set; }
    public string StandardsSnapshotId { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public byte[] OriginalBytes { get; set; } = [];
    public string Sha256 { get; set; } = "";
}

internal static class ImportedRegionalEvidenceModelConfiguration
{
    public static void Configure(ModelBuilder model) => model.Entity<ImportedRegionalEvidenceRecord>(entity =>
    {
        entity.ToTable("regional_import_evidence", table => table.HasCheckConstraint("CK_regional_import_evidence_size", "octet_length(\"OriginalBytes\") BETWEEN 1 AND 8388608"));
        entity.HasKey(row => row.Id);
        entity.HasIndex(row => new { row.ApplicationId, row.SequenceNumber, row.RelativePath }).IsUnique();
        entity.Property(row => row.SequenceNumber).HasMaxLength(16).IsRequired();
        entity.Property(row => row.StandardsSnapshotId).HasMaxLength(71).IsRequired();
        entity.Property(row => row.RelativePath).HasMaxLength(230).IsRequired();
        entity.Property(row => row.Sha256).HasMaxLength(64).IsRequired();
        entity.Property(row => row.OriginalBytes).IsRequired();
        entity.HasOne<RegionalMetadataRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.MetadataId })
            .HasPrincipalKey(row => new { row.ApplicationId, row.ScopeKey, row.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<StandardsSnapshotRecord>().WithMany().HasForeignKey(row => row.StandardsSnapshotId).OnDelete(DeleteBehavior.Restrict);
    });
}
