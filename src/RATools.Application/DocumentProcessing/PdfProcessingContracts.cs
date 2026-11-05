using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.Documents;

namespace RATools.Application.DocumentProcessing;

public enum PdfLinkTargetKind
{
    Page,
    NamedDestination,
    RelativeRemotePdf,
    ExternalUri
}

public sealed record PdfBookmarkEdit(string Title, int? PageNumber, string? NamedDestination, int Level)
{
    public PdfBookmarkEdit Normalize()
    {
        if (string.IsNullOrWhiteSpace(Title)) throw new ArgumentException("Bookmark title is required.", nameof(Title));
        if (Level is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(Level), "Bookmark depth is outside the supported range.");
        if (PageNumber is < 1) throw new ArgumentOutOfRangeException(nameof(PageNumber));
        if (PageNumber is null && string.IsNullOrWhiteSpace(NamedDestination))
            throw new ArgumentException("A bookmark requires a page or named destination.");
        if (PageNumber is not null && !string.IsNullOrWhiteSpace(NamedDestination))
            throw new ArgumentException("A bookmark cannot specify both a page and a named destination.");
        return this with { Title = Title.Trim(), NamedDestination = string.IsNullOrWhiteSpace(NamedDestination) ? null : NamedDestination.Trim() };
    }
}

public sealed record PdfLinkEdit(
    int SourcePageNumber,
    string Target,
    PdfLinkTargetKind TargetKind,
    string? ObjectPath = null,
    string? OriginalTarget = null,
    Guid? TargetPlacementId = null,
    Guid? TargetVersionId = null)
{
    public PdfLinkEdit Normalize()
    {
        if (SourcePageNumber < 1) throw new ArgumentOutOfRangeException(nameof(SourcePageNumber));
        if (string.IsNullOrWhiteSpace(Target)) throw new ArgumentException("Link target is required.", nameof(Target));
        if (string.IsNullOrWhiteSpace(ObjectPath))
            throw new ArgumentException("A link edit must bind the inspected annotation object path; page-only selection is ambiguous.", nameof(ObjectPath));
        var target = Target.Trim();
        var objectPath = ObjectPath.Trim();
        if (!objectPath.StartsWith("page[", StringComparison.Ordinal) || !objectPath.Contains("/annotation[", StringComparison.Ordinal))
            throw new ArgumentException("Link object path must identify a page annotation from the inspection result.", nameof(ObjectPath));
        if (TargetKind == PdfLinkTargetKind.RelativeRemotePdf)
        {
            if (Path.IsPathRooted(target) || Uri.TryCreate(target, UriKind.Absolute, out _))
                throw new ArgumentException("Remote PDF links must be relative paths or a relative path with a named destination.", nameof(Target));
            var filePart = target.Split('#', 2)[0];
            if (!filePart.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Remote PDF links must target a PDF file.", nameof(Target));
        }
        if (TargetKind == PdfLinkTargetKind.ExternalUri && !Uri.TryCreate(target, UriKind.Absolute, out _))
            throw new ArgumentException("External link targets must be absolute URIs.", nameof(Target));
        return this with { Target = target, ObjectPath = objectPath, OriginalTarget = string.IsNullOrWhiteSpace(OriginalTarget) ? null : OriginalTarget.Trim() };
    }
}

public sealed record PdfReferenceEdit(int SourcePageNumber, string ObjectPath, string OldReference, string NewReference, bool IsUri);

public sealed record PdfProcessingParameters(
    string? PageMode,
    IReadOnlyList<PdfBookmarkEdit>? Bookmarks,
    IReadOnlyList<PdfLinkEdit>? Links,
    IReadOnlyList<PdfReferenceEdit>? ReferenceEdits = null)
{
    public PdfProcessingParameters Normalize()
    {
        if (PageMode is not null && PageMode is not ("UseNone" or "UseOutlines" or "UseThumbs" or "FullScreen" or "UseOC" or "UseAttachments"))
            throw new ArgumentException("Unsupported PDF initial view mode.", nameof(PageMode));
        foreach (var edit in ReferenceEdits ?? [])
            if (edit.SourcePageNumber < 1 || string.IsNullOrWhiteSpace(edit.ObjectPath) ||
                string.IsNullOrWhiteSpace(edit.OldReference) || string.IsNullOrWhiteSpace(edit.NewReference))
                throw new ArgumentException("Reference rewrites require an exact annotation and old/new reference.");
        if ((ReferenceEdits ?? []).Select(edit => edit.ObjectPath).Concat((Links ?? []).Select(edit => edit.ObjectPath))
            .GroupBy(path => path, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("An annotation can be selected only once.");
        return this with
        {
            PageMode = string.IsNullOrWhiteSpace(PageMode) ? null : PageMode.Trim(),
            Bookmarks = Bookmarks?.Select(item => item.Normalize()).ToArray(),
            Links = Links?.Select(item => item.Normalize()).ToArray(),
            ReferenceEdits = ReferenceEdits?.ToArray()
        };
    }
}

public sealed record PdfSourceFacts(bool IsEncrypted, bool HasSignature, int? PageCount, bool NavigationComplete,
    bool DestinationsComplete = true, IReadOnlyList<string>? IncompleteReasons = null);

public sealed class PdfProcessingRejectedException(string message) : InvalidOperationException(message);

public sealed record PdfProcessingPlan(
    Guid Id,
    Guid SourceVersionId,
    Guid SourceDocumentId,
    Guid ApplicationId,
    Guid PlacementId,
    string SequenceNumber,
    long ExpectedWorkspaceRevision,
    string SourceSha256,
    string SourceMd5,
    int? SourcePageCount,
    string ParametersJson,
    string PlanDigest,
    string EngineVersion,
    DateTime CreatedUtc,
    string ContextJson = "{}")
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static PdfProcessingPlan Create(
        DocumentVersion source,
        Guid applicationId,
        string sequenceNumber,
        Guid placementId,
        long expectedWorkspaceRevision,
        PdfProcessingParameters parameters,
        string engineVersion,
        PdfSourceFacts? sourceFacts = null,
        string contextJson = "{}")
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.ApplicationId != applicationId || source.SequenceNumber != sequenceNumber)
            throw new ArgumentException("Source version must belong to the requested workspace.");
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        if (placementId == Guid.Empty) throw new ArgumentException("A placement identity is required.", nameof(placementId));
        ArgumentOutOfRangeException.ThrowIfNegative(expectedWorkspaceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        if (sourceFacts is { IsEncrypted: true }) throw new PdfProcessingRejectedException("Encrypted PDF processing is not supported without explicit credentials.");
        if (sourceFacts is { HasSignature: true }) throw new PdfProcessingRejectedException("Signed PDF processing is rejected because editing invalidates the signature.");
        var normalized = (parameters ?? throw new ArgumentNullException(nameof(parameters))).Normalize();
        if (sourceFacts is { NavigationComplete: false } &&
            (normalized.Bookmarks is { Count: > 0 } || normalized.Links is { Count: > 0 }))
            throw new PdfProcessingRejectedException("PDF navigation facts are incomplete; refresh the preview before editing bookmarks or links.");
        var parametersJson = JsonSerializer.Serialize(normalized, JsonOptions);
        var digest = ComputeDigest(parametersJson, applicationId, sequenceNumber, placementId, source.Id, source.Sha256, source.Md5,
            engineVersion.Trim(), expectedWorkspaceRevision, contextJson, source.SourceDocumentId, sourceFacts?.PageCount);
        return new(Guid.NewGuid(), source.Id, source.SourceDocumentId, applicationId, placementId, sequenceNumber.Trim(), expectedWorkspaceRevision,
            source.Sha256, source.Md5, sourceFacts?.PageCount, parametersJson, digest, engineVersion.Trim(), DateTime.UtcNow, contextJson);
    }

    public void Verify()
    {
        if (PlanDigest != ComputeDigest(ParametersJson, ApplicationId, SequenceNumber, PlacementId,
                SourceVersionId, SourceSha256, SourceMd5, EngineVersion, ExpectedWorkspaceRevision, ContextJson, SourceDocumentId, SourcePageCount))
            throw new InvalidDataException("The processing plan does not match its committed digest.");
    }

    public void VerifyResult(DocumentVersion result)
    {
        Verify();
        if (result.ApplicationId != ApplicationId || result.SequenceNumber != SequenceNumber ||
            result.SourceDocumentId != SourceDocumentId || result.ParentVersionId != SourceVersionId ||
            result.PlanDigest != PlanDigest || result.EngineVersion != EngineVersion || result.ParametersJson != ParametersJson)
            throw new InvalidDataException("The processing result does not belong to this plan.");
    }

    public static string ComputeDigest(string parametersJson, Guid applicationId, string sequenceNumber, Guid placementId,
        Guid sourceVersionId, string sourceSha256, string sourceMd5, string engineVersion, long expectedWorkspaceRevision,
        string contextJson = "{}", Guid sourceDocumentId = default, int? sourcePageCount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);
        if (applicationId == Guid.Empty || placementId == Guid.Empty || sourceVersionId == Guid.Empty)
            throw new ArgumentException("Processing digest identities must be nonempty.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        var input = string.Join('\n', "pdf-processing-plan-v1", applicationId.ToString("D"), sequenceNumber.Trim(), placementId.ToString("D"),
            sourceVersionId.ToString("D"), parametersJson, sourceSha256.Trim().ToLowerInvariant(), sourceMd5.Trim().ToLowerInvariant(),
            engineVersion.Trim(), expectedWorkspaceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), contextJson,
            sourceDocumentId.ToString("D"), sourcePageCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}

public enum PdfProcessingJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

public sealed record PdfProcessingJob(
    Guid Id,
    Guid PlanId,
    string PlanDigest,
    string IdempotencyKey,
    string RequestDigest,
    PdfProcessingJobStatus Status,
    string Stage,
    DateTime CreatedUtc,
    DateTime? StartedUtc = null,
    DateTime? CompletedUtc = null,
    int AttemptCount = 0,
    bool CancellationRequested = false,
    string? FailureCode = null,
    string? FailureMessage = null,
    Guid? ResultVersionId = null,
    string? ResultPath = null,
    string? ResultSha256 = null,
    string? ResultMd5 = null,
    Guid? LeaseToken = null,
    string? LeaseOwner = null,
    DateTime? LeaseExpiresUtc = null)
{
    public static PdfProcessingJob Create(PdfProcessingPlan plan, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Verify();
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var key = idempotencyKey.Trim();
        if (key.Length > 128) throw new ArgumentException("Idempotency key is too long.", nameof(idempotencyKey));
        var requestDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{plan.PlanDigest}\n{key}"))).ToLowerInvariant();
        return new(Guid.NewGuid(), plan.Id, plan.PlanDigest, key, requestDigest, PdfProcessingJobStatus.Pending, "queued", DateTime.UtcNow);
    }
}

public sealed record PdfProcessingLease(PdfProcessingJob Job, Guid Token, string Owner);

public sealed record PdfProcessingEnqueueResult(PdfProcessingJob Job, bool Created);

public sealed record PdfProcessingAdoption(Guid JobId, Guid ApplicationId, string SequenceNumber,
    Guid PlacementId, Guid DocumentId, Guid ResultVersionId, long ExpectedRevision, long WorkspaceRevision, DateTime AdoptedUtc);

public sealed class PdfProcessingIdempotencyConflictException(string key)
    : InvalidOperationException($"Idempotency key '{key}' already identifies a different PDF processing request.");

public sealed record PdfProcessingResultDetails(
    Guid JobId,
    Guid? ResultVersionId,
    string? ResultPath,
    string? Sha256,
    string? Md5,
    string? FailureCode,
    string? FailureMessage,
    PdfProcessingJobStatus Status);

public interface IDocumentProcessingStore
{
    Task AddVersionAsync(DocumentVersion version, CancellationToken cancellationToken = default);
    Task<DocumentVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> HasApplicationEvidenceAsync(Guid applicationId, string? sequenceNumber = null, CancellationToken cancellationToken = default);
    Task<bool> HasDocumentEvidenceAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task AddPlanAsync(PdfProcessingPlan plan, CancellationToken cancellationToken = default);
    Task<PdfProcessingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PdfProcessingEnqueueResult> EnqueueAsync(PdfProcessingJob job, CancellationToken cancellationToken = default);
    Task<PdfProcessingJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PdfProcessingJob>> ListJobsAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PdfProcessingAdoption?> GetAdoptionAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task AddAdoptionAsync(PdfProcessingAdoption adoption, CancellationToken cancellationToken = default);
    Task<PdfProcessingLease?> ClaimAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(Guid id, Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration, string stage, CancellationToken cancellationToken = default);
    Task<PdfProcessingJob?> CancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(Guid id, Guid token, string owner, DateTime nowUtc, DocumentVersion result, CancellationToken cancellationToken = default);
    Task<bool> FailAsync(Guid id, Guid token, string owner, DateTime nowUtc, PdfProcessingJobStatus status, string code, string message, CancellationToken cancellationToken = default);
}
