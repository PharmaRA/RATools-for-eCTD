using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;

namespace RATools.Application.Assembly;

public sealed record AssemblyPreviewRequest(long ExpectedRevision, string Manifest, string Format = "csv", string? ManifestBase64 = null);
public sealed record AssemblySelectionRequest(long ExpectedRevision, string ParentDigest, IReadOnlyList<int> SelectedRows);
public sealed record AssemblyEnqueueRequest(Guid PlanId, string PlanDigest, string IdempotencyKey);
public sealed record AssemblySourceUpload(Guid DocumentId, string SourceFile, string FileName, long ExpectedRevision,
    long Length, string Sha256, string Md5);
public sealed record AssemblySourcePage(IReadOnlyList<AssemblySourceUpload> Items, int Offset, int Limit, int Total);
public sealed record AssemblySourceBinding(int LineNumber, Guid DocumentId, Guid VersionId, string SourceFile,
    string FileName, string MediaType, long Length, string Sha256, string Md5, string TargetHref,
    string NodeBinding, string? HistoryBinding);
public sealed record AssemblyPreview(Guid Id, AssemblyPlan Plan, DateTime CreatedUtc, DateTime ExpiresUtc,
    string ManifestSha256, string Format, IReadOnlyList<int> SelectedRows, IReadOnlyList<AssemblySourceBinding> Sources,
    bool SelectionConfirmed, bool CanExecute, string InventoryDigest, string ManifestBase64, string? ParentDigest = null)
{
    public string ComputeDigest() => AssemblyEvidence.Hash(JsonSerializer.Serialize(new
    {
        Schema = "assembly-plan-v2", Id, Plan.ApplicationId, Plan.SequenceNumber, Plan.ExpectedRevision,
        CreatedUtc, ExpiresUtc, ManifestSha256, Format, SelectedRows, Sources,
        SelectionConfirmed, CanExecute, InventoryDigest, ParentDigest,
        Plan.Rows, Plan.Diagnostics, Plan.CsvVersion
    }));
    public void Verify()
    {
        if (Id == Guid.Empty || Plan.PlanDigest != ComputeDigest() ||
            AssemblyEvidence.Hash(Convert.FromBase64String(ManifestBase64)) != ManifestSha256 ||
            SelectedRows.Distinct().Count() != SelectedRows.Count ||
            !SelectedRows.SequenceEqual(SelectedRows.Order()) ||
            SelectedRows.Any(line => !Plan.Rows.Any(row => row.LineNumber == line && row.Status == AssemblyRowStatus.Valid)) ||
            (CanExecute && (!SelectionConfirmed || SelectedRows.Count == 0)))
            throw new InvalidDataException("The assembly plan evidence failed verification.");
    }
}
public sealed record AssemblyPreviewPage(Guid Id, AssemblyPlan Plan, DateTime CreatedUtc, DateTime ExpiresUtc,
    string ManifestSha256, string Format, IReadOnlyList<int> SelectedRows, bool SelectionConfirmed, bool CanExecute,
    int Offset, int Limit, int TotalRows);
public sealed record AssemblyExecutionDocument(int LineNumber, Guid PlacementId, Guid DocumentId, Guid? VersionId,
    string? Href, string LeafId, Guid? HistoricalTargetId);
public sealed record AssemblyExecutionAudit(Guid JobId, Guid PlanId, string PlanDigest, Guid ApplicationId,
    string SequenceNumber, long ExpectedRevision, long WorkspaceRevision, DateTime CompletedUtc,
    IReadOnlyList<AssemblyExecutionDocument> Documents);
public sealed record AssemblyExecutionJob(PdfProcessingJob State, AssemblyExecutionAudit? Result);
public sealed record AssemblyExecutionJobView(Guid Id, Guid PlanId, PdfProcessingJobStatus Status, string Stage,
    int AttemptCount, bool CancellationRequested, DateTime CreatedUtc, DateTime? CompletedUtc,
    string? FailureCode, string? FailureMessage, AssemblyExecutionAudit? Result);
public sealed record AssemblyEnqueueResult(AssemblyExecutionJob Job, bool Created);

public interface IAssemblyStore
{
    Task AddPlanAsync(AssemblyPreview plan, CancellationToken cancellationToken = default);
    Task<AssemblyPreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssemblyEnqueueResult> EnqueueAsync(PdfProcessingJob state, CancellationToken cancellationToken = default);
    Task<AssemblyExecutionJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssemblyExecutionJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan duration, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan duration, CancellationToken cancellationToken = default);
    Task<AssemblyExecutionJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, AssemblyExecutionAudit result, CancellationToken cancellationToken = default);
    Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status, string code, string message, CancellationToken cancellationToken = default);
}
public static class AssemblyEvidence
{
    public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    public static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static PdfProcessingJob Request(AssemblyPreview plan, string key)
    {
        plan.Verify(); ArgumentException.ThrowIfNullOrWhiteSpace(key); key = key.Trim();
        if (key.Length > 128) throw new ArgumentException("The assembly idempotency key is too long.", nameof(key));
        return new(Guid.NewGuid(), plan.Id, plan.Plan.PlanDigest, key, Hash(plan.Id + "\n" + plan.Plan.PlanDigest + "\n" + key),
            PdfProcessingJobStatus.Pending, "queued", DateTime.UtcNow);
    }
}
