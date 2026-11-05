using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class DocumentVersionRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid SourceDocumentId { get; set; }
    public Guid? ParentVersionId { get; set; }
    public string Data { get; set; } = "";
    public string Digest { get; set; } = "";
}

public sealed class PdfProcessingPlanRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid SourceVersionId { get; set; }
    public string Data { get; set; } = "";
    public string Digest { get; set; } = "";
}

public sealed class PdfProcessingJobRecord
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string PlanDigest { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestDigest { get; set; } = "";
    public PdfProcessingJobStatus Status { get; set; }
    public string Stage { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int AttemptCount { get; set; }
    public bool CancellationRequested { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public Guid? ResultVersionId { get; set; }
    public string? ResultPath { get; set; }
    public string? ResultSha256 { get; set; }
    public string? ResultMd5 { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
}

public sealed class PdfProcessingAdoptionRecord
{
    public Guid JobId { get; set; }
    public Guid DocumentId { get; set; }
    public string Data { get; set; } = "";
    public string Digest { get; set; } = "";
}

internal static class DocumentProcessingModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<DocumentVersionRecord>(entity =>
        {
            entity.ToTable("document_versions"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Data).IsRequired(); entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentRecord>().WithMany().HasForeignKey(row => row.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentVersionRecord>().WithMany().HasForeignKey(row => row.ParentVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PdfProcessingPlanRecord>(entity =>
        {
            entity.ToTable("document_processing_plans"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Data).IsRequired(); entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.HasOne<ApplicationRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentVersionRecord>().WithMany().HasForeignKey(row => row.SourceVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PdfProcessingJobRecord>(entity =>
        {
            entity.ToTable("document_processing_jobs", table =>
            {
                table.HasCheckConstraint("CK_processing_attempts", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 3");
                table.HasCheckConstraint("CK_processing_lease", "\"Status\" <> 'Running' OR (\"LeaseToken\" IS NOT NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresUtc\" IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.IdempotencyKey).IsUnique(); entity.HasIndex(row => row.PlanId).IsUnique();
            entity.HasIndex(row => new { row.Status, row.CreatedUtc });
            entity.Property(row => row.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PlanDigest).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(row => row.Stage).HasMaxLength(64).IsRequired();
            entity.Property(row => row.FailureCode).HasMaxLength(128);
            entity.Property(row => row.FailureMessage).HasMaxLength(2048);
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.ResultSha256).HasMaxLength(64);
            entity.Property(row => row.ResultMd5).HasMaxLength(32);
            entity.HasOne<PdfProcessingPlanRecord>().WithMany().HasForeignKey(row => row.PlanId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentVersionRecord>().WithMany().HasForeignKey(row => row.ResultVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PdfProcessingAdoptionRecord>(entity =>
        {
            entity.ToTable("document_processing_adoptions"); entity.HasKey(row => row.JobId);
            entity.Property(row => row.Data).IsRequired(); entity.Property(row => row.Digest).HasMaxLength(64).IsRequired();
            entity.HasOne<PdfProcessingJobRecord>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentRecord>().WithMany().HasForeignKey(row => row.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal static class ProcessingRecordData
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static T Read<T>(string json, string digest)
    {
        if (Hash(json) != digest) throw new InvalidDataException("Stored PDF processing evidence failed its integrity check.");
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Stored PDF processing evidence is empty.");
    }

    public static DocumentVersionRecord Record(DocumentVersion version)
    {
        var data = JsonSerializer.Serialize(new VersionData(version.Id, version.ApplicationId, version.SequenceNumber,
            version.SourceDocumentId, version.ParentVersionId, version.FileName, version.MediaType, version.FileSize,
            version.Sha256, version.Md5, version.StoragePath, version.CreatedUtc, version.EngineVersion, version.ParametersJson, version.PlanDigest));
        return new() { Id = version.Id, ApplicationId = version.ApplicationId, SourceDocumentId = version.SourceDocumentId,
            ParentVersionId = version.ParentVersionId, Data = data, Digest = Hash(data) };
    }

    public static DocumentVersion Version(DocumentVersionRecord row)
    {
        var value = Read<VersionData>(row.Data, row.Digest);
        if (value.Id != row.Id || value.ApplicationId != row.ApplicationId || value.SourceDocumentId != row.SourceDocumentId || value.ParentVersionId != row.ParentVersionId)
            throw new InvalidDataException("Stored PDF version identity changed.");
        return DocumentVersion.Rehydrate(value.Id, value.ApplicationId, value.SequenceNumber, value.SourceDocumentId,
            value.ParentVersionId, value.FileName, value.MediaType, value.FileSize, value.Sha256, value.Md5,
            value.StoragePath, value.CreatedUtc, value.EngineVersion, value.ParametersJson, value.PlanDigest);
    }

    public static PdfProcessingJob Job(PdfProcessingJobRecord row) => new(row.Id, row.PlanId, row.PlanDigest,
        row.IdempotencyKey, row.RequestDigest, row.Status, row.Stage, row.CreatedUtc, row.StartedUtc, row.CompletedUtc,
        row.AttemptCount, row.CancellationRequested, row.FailureCode, row.FailureMessage, row.ResultVersionId,
        row.ResultPath, row.ResultSha256, row.ResultMd5, row.LeaseToken, row.LeaseOwner, row.LeaseExpiresUtc);

    private sealed record VersionData(Guid Id, Guid ApplicationId, string SequenceNumber, Guid SourceDocumentId,
        Guid? ParentVersionId, string FileName, string MediaType, long FileSize, string Sha256, string Md5,
        string StoragePath, DateTime CreatedUtc, string EngineVersion, string ParametersJson, string PlanDigest);
}
