using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Ctd;

namespace RATools.Infrastructure.Persistence.EfCore;

internal static class CtdModelConfiguration
{
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CtdDefinitionRecord>(entity =>
        {
            entity.ToTable("ctd_definitions");
            entity.HasKey(row => new { row.Version, row.DefinitionKey });
            entity.Property(row => row.Version).HasMaxLength(64);
            entity.Property(row => row.DefinitionKey).HasMaxLength(256);
            entity.Property(row => row.ParentDefinitionKey).HasMaxLength(256);
            entity.Property(row => row.SectionPath).HasMaxLength(128);
            entity.Property(row => row.Kind).HasMaxLength(32);
            entity.Property(row => row.ExtensionPolicy).HasMaxLength(32);
            entity.Property(row => row.SchemaJson).HasColumnType("jsonb").IsRequired();
            entity.HasAlternateKey(row => new { row.Version, row.DefinitionKey, row.Repeatable, row.Kind }).HasName("AK_ctd_definitions_shape");
            entity.HasData(IchSectionDefinitions.Current.Definitions.Values.Select(definition => new CtdDefinitionRecord
            {
                Version = IchSectionDefinitions.Version, DefinitionKey = definition.DefinitionKey,
                ParentDefinitionKey = definition.ParentDefinitionKey, SectionPath = definition.SectionPath,
                Repeatable = definition.Repeatable, Kind = definition.Kind.ToString(), AllowsLeaves = definition.AllowsLeaves,
                ExtensionPolicy = definition.ExtensionPolicy.ToString(), SchemaJson = JsonSerializer.Serialize(definition, SchemaJsonOptions)
            }));
        });

        modelBuilder.Entity<CtdNodeInstanceRecord>(entity =>
        {
            entity.ToTable("ctd_node_instances", table =>
            {
                table.HasCheckConstraint("CK_ctd_nodes_no_self_parent", "\"ParentInstanceId\" IS NULL OR \"ParentInstanceId\" <> \"Id\"");
                table.HasCheckConstraint("CK_ctd_nodes_identity_status", "\"IdentityStatus\" IN ('Resolved', 'MissingMetadata', 'Ambiguous')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.ApplicationId, row.Id }).HasName("AK_ctd_nodes_application_id");
            entity.HasAlternateKey(row => new { row.ApplicationId, row.Id, row.DefinitionVersion }).HasName("AK_ctd_nodes_application_id_version");
            entity.Property(row => row.DefinitionVersion).HasMaxLength(64);
            entity.Property(row => row.DefinitionKey).HasMaxLength(256);
            entity.Property(row => row.Kind).HasMaxLength(32);
            entity.Property(row => row.IdentityKey).HasMaxLength(64);
            entity.Property(row => row.IdentityComparisonVersion).HasMaxLength(64);
            entity.Property(row => row.IdentityStatus).HasMaxLength(32);
            entity.Property(row => row.IdentityAttributesJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne<ApplicationRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CtdDefinitionRecord>().WithMany()
                .HasForeignKey(row => new { row.DefinitionVersion, row.DefinitionKey, row.Repeatable, row.Kind })
                .HasPrincipalKey(row => new { row.Version, row.DefinitionKey, row.Repeatable, row.Kind }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CtdNodeInstanceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.ParentInstanceId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.Id }).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(row => new { row.ApplicationId, row.ParentInstanceId, row.DefinitionKey }, "IX_ctd_nodes_single_child")
                .IsUnique().HasFilter("NOT \"Repeatable\" AND \"ParentInstanceId\" IS NOT NULL");
            entity.HasIndex(row => new { row.ApplicationId, row.DefinitionKey }, "IX_ctd_nodes_single_root")
                .IsUnique().HasFilter("NOT \"Repeatable\" AND \"ParentInstanceId\" IS NULL");
            entity.HasIndex(row => new { row.ApplicationId, row.IdentityKey }, "IX_ctd_nodes_resolved_identity")
                .IsUnique().HasFilter("\"IdentityStatus\" <> 'Ambiguous'");
        });

        modelBuilder.Entity<SequenceNodeRecord>(entity =>
        {
            entity.ToTable("sequence_nodes", table =>
            {
                table.HasCheckConstraint("CK_sequence_nodes_sort_order", "\"SortOrder\" >= 0");
                table.HasCheckConstraint("CK_sequence_nodes_metadata_status", "\"MetadataStatus\" IN ('Complete', 'NeedsMetadataCompletion', 'LegacyUnresolved')");
            });
            entity.HasKey(row => new { row.ApplicationId, row.SequenceNumber, row.NodeInstanceId });
            entity.HasAlternateKey(row => new { row.ApplicationId, row.SequenceNumber, row.NodeInstanceId, row.CtdSection })
                .HasName("AK_sequence_nodes_scope_section");
            entity.Property(row => row.SequenceNumber).HasMaxLength(16);
            entity.Property(row => row.DefinitionVersion).HasMaxLength(64);
            entity.Property(row => row.CtdSection).HasMaxLength(128);
            entity.Property(row => row.AttributesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Title).HasMaxLength(1024);
            entity.Property(row => row.StorageSegment).HasMaxLength(255);
            entity.Property(row => row.MetadataStatus).HasMaxLength(32);
            entity.HasOne<SequenceRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CtdNodeInstanceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.NodeInstanceId, row.DefinitionVersion })
                .HasPrincipalKey(row => new { row.ApplicationId, row.Id, row.DefinitionVersion }).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<SequenceNodeRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.ParentNodeInstanceId }).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(row => new { row.ApplicationId, row.SequenceNumber, row.ParentNodeInstanceId, row.SortOrder });
        });

        modelBuilder.Entity<NodeBackfillCheckpointRecord>(entity =>
        {
            entity.ToTable("node_backfill_checkpoints");
            entity.HasKey(row => new { row.ApplicationId, row.SequenceNumber, row.Version });
            entity.Property(row => row.SequenceNumber).HasMaxLength(16);
            entity.Property(row => row.Version).HasMaxLength(64);
            entity.Property(row => row.InputDigest).HasMaxLength(64);
            entity.HasOne<SequenceRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeBackfillDiagnosticRecord>(entity =>
        {
            entity.ToTable("node_backfill_diagnostics");
            entity.HasKey(row => new { row.ApplicationId, row.SequenceNumber, row.PlacementId, row.Code });
            entity.Property(row => row.SequenceNumber).HasMaxLength(16);
            entity.Property(row => row.Code).HasMaxLength(64);
            entity.Property(row => row.Message).HasMaxLength(1024);
            entity.HasOne<DocumentPlacementRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.SequenceNumber, row.PlacementId })
                .HasPrincipalKey(row => new { row.ApplicationId, row.SequenceNumber, row.Id }).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
