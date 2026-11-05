using Microsoft.EntityFrameworkCore;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class StandardsSnapshotRecord
{
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string TemplateKey { get; set; } = "";
    public string RuleProfileAlias { get; set; } = "";
    public DateTime AcquiredUtc { get; set; }
    public string Data { get; set; } = "";
    public string DataSha256 { get; set; } = "";
}

internal static class StandardsModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StandardsSnapshotRecord>(entity =>
        {
            entity.ToTable("standards_profile_snapshots", table =>
            {
                table.HasCheckConstraint("CK_standards_snapshot_id", "\"Id\" ~ '^sha256:[0-9a-f]{64}$'");
                table.HasCheckConstraint("CK_standards_snapshot_schema", "\"SchemaVersion\" = 1");
                table.HasCheckConstraint("CK_standards_snapshot_hash", "\"DataSha256\" ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint("CK_standards_snapshot_size", "octet_length(\"Data\") <= 67108864");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(71).IsRequired();
            entity.Property(row => row.TemplateKey).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RuleProfileAlias).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Data).IsRequired();
            entity.Property(row => row.DataSha256).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.TemplateKey, row.RuleProfileAlias, row.AcquiredUtc })
                .HasDatabaseName("IX_standards_snapshot_template_profile_acquired");
        });
    }
}
