using Microsoft.EntityFrameworkCore;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Infrastructure.Persistence.EfCore;

internal static class PackageValidationModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PackageValidationInputRecord>(entity =>
        {
            entity.ToTable("package_validation_inputs");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.SequenceNumber).HasMaxLength(4);
            entity.Property(row => row.SourcePath).HasMaxLength(4096);
            entity.Property(row => row.RootRelativePath).HasMaxLength(4096);
            entity.Property(row => row.SourceKind).HasMaxLength(16);
            entity.Property(row => row.InputDigest).HasMaxLength(64);
            entity.HasIndex(row => new { row.ApplicationId, row.CreatedUtc });
        });
        modelBuilder.Entity<PackageValidationRun>(entity =>
        {
            entity.ToTable("package_validation_runs", table =>
            {
                table.HasCheckConstraint("CK_package_validation_run_revision", "\"Revision\" >= 0 AND \"AttemptCount\" >= 0");
                table.HasCheckConstraint("CK_package_validation_run_status", "\"Status\" IN ('Pending', 'Running', 'Completed', 'Failed', 'Cancelled', 'InputChanged')");
                table.HasCheckConstraint("CK_package_validation_run_lease", "(\"Status\" = 'Running') = (\"LeaseToken\" IS NOT NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresUtc\" IS NOT NULL)");
                table.HasCheckConstraint("CK_package_validation_run_completion", "(\"Status\" IN ('Pending', 'Running')) = (\"CompletedUtc\" IS NULL)");
                table.HasCheckConstraint("CK_package_validation_run_report", "(\"Status\" = 'Completed') = (\"ReportSha256\" IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.SequenceNumber).HasMaxLength(4);
            entity.Property(row => row.Binding).HasConversion(value => PackageValidationJson.Write(value), value => PackageValidationJson.Read<ValidationBinding>(value));
            entity.Property(row => row.RequestDigest).HasMaxLength(64);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(200);
            entity.HasIndex(row => row.IdempotencyKey).IsUnique();
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(row => row.Stage).HasMaxLength(32);
            entity.Property(row => row.LeaseOwner).HasMaxLength(200);
            entity.Property(row => row.FailureCode).HasMaxLength(128);
            entity.Property(row => row.ReportSha256).HasMaxLength(64);
            entity.Property(row => row.CoverageSummary).HasConversion(
                value => PackageValidationJson.Write(value), value => PackageValidationJson.Read<ValidationCoverageSummary>(value));
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.Ignore(row => row.IsTerminal);
            entity.HasIndex(row => new { row.Status, row.CreatedUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.CreatedUtc });
        });
        modelBuilder.Entity<PackageValidationReportRecord>(entity =>
        {
            entity.ToTable("package_validation_reports");
            entity.HasKey(row => row.RunId);
            entity.HasOne<PackageValidationRun>().WithOne().HasForeignKey<PackageValidationReportRecord>(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PackageValidationFindingRecord>(entity =>
        {
            entity.ToTable("package_validation_findings");
            entity.HasKey(row => new { row.RunId, row.Ordinal });
            entity.Property(row => row.RuleId).HasMaxLength(128);
            entity.Property(row => row.Severity).HasMaxLength(16);
            entity.Property(row => row.Status).HasMaxLength(16);
            entity.HasIndex(row => new { row.RunId, row.RuleId, row.Severity, row.Status });
            entity.HasOne<PackageValidationReportRecord>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PackageValidationProjectionRecord>(entity =>
        {
            entity.ToTable("package_validation_projections");
            entity.HasKey(row => row.RunId);
            entity.Property(row => row.Sha256).HasMaxLength(64).IsRequired();
            entity.HasOne<PackageValidationReportRecord>().WithOne().HasForeignKey<PackageValidationProjectionRecord>(row => row.RunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PackageValidationExternalComparisonRecord>(entity =>
        {
            entity.ToTable("package_validation_external_comparisons");
            // One reviewed external report per run; a corrected report requires a new run.
            entity.HasKey(row => row.RunId);
            entity.Property(row => row.Id).IsRequired();
            entity.Property(row => row.ToolName).HasMaxLength(128).IsRequired();
            entity.Property(row => row.ToolVersion).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PackageDigest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ReportSha256).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RecordedUtc).IsRequired();
            entity.HasOne<PackageValidationReportRecord>().WithOne().HasForeignKey<PackageValidationExternalComparisonRecord>(row => row.RunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
