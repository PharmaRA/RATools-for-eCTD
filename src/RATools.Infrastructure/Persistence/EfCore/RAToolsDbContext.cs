using Microsoft.EntityFrameworkCore;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class RAToolsDbContext(DbContextOptions<RAToolsDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationRecord> Applications => Set<ApplicationRecord>();

    public DbSet<SequenceRecord> Sequences => Set<SequenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationRecord>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApplicationNumber).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Region).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SponsorName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
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
            entity.Property(x => x.CreatedUtc).IsRequired();
        });
    }
}
