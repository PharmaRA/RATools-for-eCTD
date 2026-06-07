using Microsoft.EntityFrameworkCore;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class RAToolsDbContext(DbContextOptions<RAToolsDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationRecord> Applications => Set<ApplicationRecord>();

    public DbSet<SequenceRecord> Sequences => Set<SequenceRecord>();

    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();

    public DbSet<DocumentPlacementRecord> DocumentPlacements => Set<DocumentPlacementRecord>();

    public DbSet<PublishJobRecord> PublishJobs => Set<PublishJobRecord>();

    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationRecord>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApplicationNumber).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EctdTemplateKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Region).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SponsorName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.WorkingDirectoryPath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.HasIndex(x => x.ApplicationNumber);
            entity.HasMany(x => x.Sequences)
                .WithOne()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SequenceRecord>(entity =>
        {
            entity.ToTable("sequences");
            entity.HasKey(x => new { x.ApplicationId, x.SequenceNumber });
            entity.Property(x => x.SequenceNumber).HasMaxLength(16).IsRequired();
            entity.Property(x => x.SubmissionType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.FdaApplicationType).HasMaxLength(32);
            entity.Property(x => x.FdaSubmissionType).HasMaxLength(128);
            entity.Property(x => x.FdaSubmissionSubtype).HasMaxLength(128);
            entity.Property(x => x.FdaSequenceDescription).HasMaxLength(512);
            entity.Property(x => x.FdaApplicantName).HasMaxLength(256);
            entity.Property(x => x.FdaFormType).HasMaxLength(128);
            entity.Property(x => x.CreatedUtc).IsRequired();
        });

        modelBuilder.Entity<DocumentRecord>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.MediaType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.FileSize).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(128).IsRequired();
            entity.Property(x => x.StoragePath).HasMaxLength(512).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
        });

        modelBuilder.Entity<DocumentPlacementRecord>(entity =>
        {
            entity.ToTable("document_placements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DocumentId).IsRequired();
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.SequenceNumber).HasMaxLength(16).IsRequired();
            entity.Property(x => x.CtdSection).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Operation).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(512);
            entity.Property(x => x.LifecycleTargetPlacementId);
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.SequenceNumber });
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => x.LifecycleTargetPlacementId);
            entity.HasOne<ApplicationRecord>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SequenceRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.SequenceNumber })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DocumentRecord>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PublishJobRecord>(entity =>
        {
            entity.ToTable("publish_jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApplicationId).IsRequired();
            entity.Property(x => x.SequenceNumber).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.OutputPath).HasMaxLength(512);
            entity.Property(x => x.PackagePath).HasMaxLength(512);
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.Property(x => x.FailureReason).HasMaxLength(1024);
            entity.HasIndex(x => new { x.ApplicationId, x.CreatedUtc });
            entity.HasIndex(x => new { x.ApplicationId, x.SequenceNumber, x.CreatedUtc });
            entity.HasIndex(x => new { x.ApplicationId, x.SequenceNumber, x.Status });
            entity.HasIndex(x => new { x.ApplicationId, x.SequenceNumber })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Pending', 'Running')");
        });

        modelBuilder.Entity<AuditLogRecord>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Actor).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(2048);
            entity.Property(x => x.CreatedUtc).IsRequired();
        });
    }
}
