using RATools.Domain.Documents;
using System.Security.Cryptography;
using System.Text;

namespace RATools.Application.DocumentProcessing;

public sealed record PdfRenamePreviewRequest(long ExpectedRevision, IReadOnlyList<PdfRelocationChange> Changes,
    IReadOnlyList<Guid> ConfirmedSources);
public sealed record PdfRenameOutput(Guid PlacementId, Guid VersionId, string Href, string Sha256, string Md5);
public sealed record PdfRenameAdoptedDocument(Guid PlacementId, Guid DocumentId, Guid ResultVersionId, string Href,
    Guid? NodeInstanceId);
public sealed record PdfRenameAdoption(Guid JobId, Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    long WorkspaceRevision, DateTime AdoptedUtc, IReadOnlyList<PdfRenameAdoptedDocument> Documents);
public sealed record PdfRenameJobView(Guid Id, Guid PlanId, PdfProcessingJobStatus Status, string Stage, int AttemptCount,
    bool CancellationRequested, DateTime CreatedUtc, DateTime? CompletedUtc, string? FailureCode, string? FailureMessage,
    IReadOnlyList<PdfRenameOutput> Results, PdfRenameAdoption? Adoption);
public sealed record PdfRenamePlacementBinding(Guid PlacementId, Guid DocumentId, string SequenceNumber, string Href,
    string Sha256, string Md5, long FileSize, string MediaType, DocumentPlacementOperation Operation, Guid? NodeInstanceId,
    string CtdSection, int SortOrder, string LeafId, Guid? LifecycleTargetPlacementId, string? Title);
public sealed record PdfRenameJob(PdfProcessingJob State, IReadOnlyList<PdfRenameOutput> Results);
public sealed record PdfRenameEnqueueResult(PdfRenameJob Job, bool Created);

public static class PdfRenameRequests
{
    public static PdfProcessingJob Create(PdfRenamePreview preview, string key)
    {
        preview.Verify();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        key = key.Trim();
        if (key.Length > 128) throw new ArgumentException("Idempotency key exceeds its length limit.", nameof(key));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preview.Id + "\n" + preview.Plan.PlanDigest + "\n" + key))).ToLowerInvariant();
        return new(Guid.NewGuid(), preview.Id, preview.Plan.PlanDigest, key, digest, PdfProcessingJobStatus.Pending, "queued", DateTime.UtcNow);
    }
}

public interface IPdfRenameStore
{
    Task AddPlanAsync(PdfRenamePreview plan, CancellationToken cancellationToken = default);
    Task<PdfRenamePreview?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PdfRenameEnqueueResult> EnqueueAsync(PdfProcessingJob job, CancellationToken cancellationToken = default);
    Task<PdfRenameJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PdfRenameJob?> GetPlanJobAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan duration, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan duration, string stage, CancellationToken cancellationToken = default);
    Task<PdfRenameJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, IReadOnlyList<PdfRenameOutput> results, CancellationToken cancellationToken = default);
    Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status, string code, string message, CancellationToken cancellationToken = default);
    Task<PdfRenameAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task AddAdoptionAsync(PdfRenameAdoption adoption, CancellationToken cancellationToken = default);
}
