using Microsoft.EntityFrameworkCore;
using RATools.Application.DocumentProcessing;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class AssemblyPlanRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Data { get; set; } = "";
    public string Digest { get; set; } = "";
}
public sealed class AssemblyExecutionRecord
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public PdfProcessingJobStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public DateTime? QuarantinedUtc { get; set; }
    public string Data { get; set; } = "";
    public string Digest { get; set; } = "";
}
internal static class AssemblyModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<AssemblyPlanRecord>(entity =>
        {
            entity.ToTable("assembly_plans"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Data).IsRequired(); entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AssemblyExecutionRecord>(entity =>
        {
            entity.ToTable("assembly_executions"); entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.IdempotencyKey).IsUnique(); entity.HasIndex(row => row.PlanId).IsUnique();
            entity.HasIndex(row => new { row.Status, row.CreatedUtc });
            entity.Property(row => row.Data).IsRequired(); entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasOne<AssemblyPlanRecord>().WithMany().HasForeignKey(row => row.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
