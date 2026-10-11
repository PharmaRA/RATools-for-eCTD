using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Domain.PackageValidation;

// Persisted independently of applications, sequences and publish jobs. An external
// package need not have business records, and deleting a workspace cannot delete its evidence.
public sealed class PackageValidationRun
{
    private PackageValidationRun() { }

    public PackageValidationRun(Guid applicationId, string sequenceNumber, ValidationBinding binding,
        string requestJson, string idempotencyKey, DateTime createdUtc)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        PackageInputManifest.RequireSequenceNumber(sequenceNumber);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 200 || idempotencyKey != idempotencyKey.Trim())
            throw new ArgumentException("Use an exact idempotency key of at most 200 characters without surrounding whitespace.", nameof(idempotencyKey));
        RequireUtc(createdUtc);
        using var request = JsonDocument.Parse(requestJson);
        if (request.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("The request must be a JSON object.", nameof(requestJson));
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber;
        Binding = binding;
        RequestJson = requestJson;
        RequestDigest = ComputeRequestDigest(request.RootElement);
        IdempotencyKey = idempotencyKey;
        CreatedUtc = createdUtc;
    }

    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string SequenceNumber { get; private set; } = "";
    public ValidationBinding Binding { get; private set; } = null!;
    public string RequestJson { get; private set; } = "";
    public string RequestDigest { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public DateTime CreatedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public ValidationRunStatus Status { get; private set; } = ValidationRunStatus.Pending;
    public string Stage { get; private set; } = "Queued";
    public int AttemptCount { get; private set; }
    public long Revision { get; private set; }
    public DateTime? CancellationRequestedUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public string? ReportSha256 { get; private set; }
    public ValidationCoverageSummary? CoverageSummary { get; private set; }
    public bool IsTerminal => Status is not (ValidationRunStatus.Pending or ValidationRunStatus.Running);

    public PackageValidationRun Copy() => (PackageValidationRun)MemberwiseClone();

    public void VerifyRequestIntegrity()
    {
        using var request = JsonDocument.Parse(RequestJson);
        if (ComputeRequestDigest(request.RootElement) != RequestDigest)
            throw new InvalidDataException("The persisted validation request differs from its committed digest.");
    }

    private string ComputeRequestDigest(JsonElement request) => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
    {
        schemaVersion = 1, applicationId = ApplicationId.ToString("D"), sequenceNumber = SequenceNumber,
        binding = Binding, request
    }));

    public Guid Claim(string owner, DateTime nowUtc, TimeSpan leaseDuration)
    {
        RequireUtc(nowUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 200) throw new ArgumentException("The worker identity is too long.", nameof(owner));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        if (Status != ValidationRunStatus.Pending || CancellationRequestedUtc is not null)
            throw new InvalidOperationException("Only a pending run without cancellation can be claimed.");
        Status = ValidationRunStatus.Running;
        Stage = "ReadingInputs";
        StartedUtc = nowUtc;
        AttemptCount = checked(AttemptCount + 1);
        LeaseOwner = owner;
        LeaseToken = Guid.NewGuid();
        LeaseExpiresUtc = nowUtc.Add(leaseDuration);
        FailureCode = null;
        FailureMessage = null;
        Touch();
        return LeaseToken.Value;
    }

    public bool OwnsLease(Guid token, string owner, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        return Status == ValidationRunStatus.Running && token != Guid.Empty && LeaseToken == token &&
            LeaseOwner == owner && LeaseExpiresUtc > nowUtc;
    }

    public void Heartbeat(Guid token, string owner, DateTime nowUtc, TimeSpan leaseDuration, string stage)
    {
        EnsureLease(token, owner, nowUtc);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        if (stage is not ("ReadingInputs" or "Inspecting" or "Archiving")) throw new ArgumentException("Unknown execution stage.", nameof(stage));
        LeaseExpiresUtc = nowUtc.Add(leaseDuration);
        Stage = stage;
        Touch();
    }

    public bool RequestCancellation(DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (IsTerminal || CancellationRequestedUtc is not null) return false;
        CancellationRequestedUtc = nowUtc;
        if (Status == ValidationRunStatus.Pending) End(ValidationRunStatus.Cancelled, nowUtc, "CANCELLED", "Cancelled before execution.");
        else Touch();
        return true;
    }

    public void Complete(Guid token, string owner, DateTime nowUtc, ValidationReport report, string reportSha256)
    {
        EnsureLease(token, owner, nowUtc);
        ArgumentNullException.ThrowIfNull(report);
        ValidationBinding.RequireDigest(reportSha256);
        if (report.Binding != Binding || report.RunStatus != ValidationRunStatus.Completed)
            throw new ArgumentException("The completed report must match the entire requested validation binding.", nameof(report));
        if (CancellationRequestedUtc is not null) throw new InvalidOperationException("A cancellation request prevents successful completion.");
        ReportSha256 = reportSha256;
        CoverageSummary = report.Summary;
        End(ValidationRunStatus.Completed, nowUtc, null, null);
    }

    public void Fail(Guid token, string owner, DateTime nowUtc, ValidationRunStatus status, string code, string message)
    {
        EnsureLease(token, owner, nowUtc);
        if (status is not (ValidationRunStatus.Failed or ValidationRunStatus.Cancelled or ValidationRunStatus.InputChanged))
            throw new ArgumentException("Execution failure requires a non-success terminal status.", nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        End(CancellationRequestedUtc is not null ? ValidationRunStatus.Cancelled : status, nowUtc,
            CancellationRequestedUtc is not null ? "CANCELLED" : code,
            CancellationRequestedUtc is not null ? "Cancellation was acknowledged by the worker." : message);
    }

    public bool RecoverExpiredLease(DateTime nowUtc, int maxAttempts)
    {
        RequireUtc(nowUtc);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        if (Status != ValidationRunStatus.Running || LeaseExpiresUtc > nowUtc) return false;
        if (CancellationRequestedUtc is not null)
            End(ValidationRunStatus.Cancelled, nowUtc, "CANCELLED", "Cancellation recovered after the worker lease expired.");
        else if (AttemptCount >= maxAttempts)
            End(ValidationRunStatus.Failed, nowUtc, "WORKER_LEASE_EXPIRED", "Worker lease expired; retry attempts are exhausted.");
        else
        {
            Status = ValidationRunStatus.Pending;
            Stage = "Queued";
            LeaseOwner = null;
            LeaseToken = null;
            LeaseExpiresUtc = null;
            FailureCode = "WORKER_LEASE_EXPIRED";
            FailureMessage = "The next attempt must reread every input and produce a new report.";
            Touch();
        }
        return true;
    }

    private void End(ValidationRunStatus status, DateTime nowUtc, string? code, string? message)
    {
        Status = status;
        Stage = status.ToString();
        CompletedUtc = nowUtc;
        FailureCode = code;
        FailureMessage = message;
        LeaseOwner = null;
        LeaseToken = null;
        LeaseExpiresUtc = null;
        Touch();
    }

    private void EnsureLease(Guid token, string owner, DateTime nowUtc)
    {
        if (!OwnsLease(token, owner, nowUtc)) throw new InvalidOperationException("The worker does not own a live validation lease.");
    }

    private void Touch() => Revision = checked(Revision + 1);
    private static void RequireUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc) throw new ArgumentException("An explicit UTC timestamp is required.");
    }
}
