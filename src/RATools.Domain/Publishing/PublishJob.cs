using RATools.Domain.Common;

namespace RATools.Domain.Publishing;

public sealed class PublishJob : Entity
{
    public PublishJob(Guid applicationId, string sequenceNumber, string? idempotencyKey = null)
        : this(
            Guid.NewGuid(),
            applicationId,
            sequenceNumber,
            PublishJobStatus.Pending,
            null,
            null,
            DateTime.UtcNow,
            null,
            null,
            idempotencyKey,
            0,
            null,
            null,
            null,
            null,
            null)
    {
    }

    private PublishJob(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        PublishJobStatus status,
        string? outputPath,
        string? packagePath,
        DateTime createdUtc,
        DateTime? completedUtc,
        string? failureReason,
        string? idempotencyKey,
        int attemptCount,
        DateTime? nextAttemptUtc,
        string? leaseOwner,
        Guid? leaseToken,
        DateTime? leaseExpiresUtc,
        DateTime? lastHeartbeatUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);

        Id = id;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber.Trim();
        Status = status;
        OutputPath = outputPath?.Trim();
        PackagePath = packagePath?.Trim();
        CreatedUtc = createdUtc;
        CompletedUtc = completedUtc;
        FailureReason = failureReason?.Trim();
        IdempotencyKey = NormalizeIdempotencyKey(idempotencyKey, id);
        AttemptCount = attemptCount;
        NextAttemptUtc = nextAttemptUtc ?? createdUtc;
        LeaseOwner = leaseOwner?.Trim();
        LeaseToken = leaseToken;
        LeaseExpiresUtc = leaseExpiresUtc;
        LastHeartbeatUtc = lastHeartbeatUtc;
    }

    public Guid ApplicationId { get; private set; }

    public string SequenceNumber { get; private set; }

    public PublishJobStatus Status { get; private set; }

    public string? OutputPath { get; private set; }

    public string? PackagePath { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime? CompletedUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public string IdempotencyKey { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTime NextAttemptUtc { get; private set; }

    public string? LeaseOwner { get; private set; }

    public Guid? LeaseToken { get; private set; }

    public DateTime? LeaseExpiresUtc { get; private set; }

    public DateTime? LastHeartbeatUtc { get; private set; }

    public void MarkRunning()
    {
        if (Status != PublishJobStatus.Pending)
        {
            throw new InvalidOperationException($"Publish job can only move to Running from Pending. Current status: {Status}.");
        }

        Status = PublishJobStatus.Running;
        FailureReason = null;
    }

    public Guid Claim(string owner, DateTime nowUtc, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        if (Status != PublishJobStatus.Pending || NextAttemptUtc > nowUtc)
        {
            throw new InvalidOperationException($"Publish job {Id} is not ready to be claimed.");
        }

        var token = Guid.NewGuid();
        Status = PublishJobStatus.Running;
        AttemptCount++;
        LeaseOwner = owner.Trim();
        LeaseToken = token;
        LastHeartbeatUtc = nowUtc;
        LeaseExpiresUtc = nowUtc.Add(leaseDuration);
        FailureReason = null;
        return token;
    }

    public void RenewLease(Guid leaseToken, string owner, DateTime nowUtc, TimeSpan leaseDuration)
    {
        EnsureLease(leaseToken, owner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        if (LeaseExpiresUtc <= nowUtc)
        {
            throw new InvalidOperationException($"Publish job {Id} lease has expired.");
        }

        LastHeartbeatUtc = nowUtc;
        LeaseExpiresUtc = nowUtc.Add(leaseDuration);
    }

    public void ScheduleRetry(Guid leaseToken, string owner, string failureReason, DateTime nextAttemptUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        EnsureLease(leaseToken, owner);

        Status = PublishJobStatus.Pending;
        FailureReason = failureReason.Trim();
        CompletedUtc = null;
        NextAttemptUtc = nextAttemptUtc;
        ClearLease();
    }

    public void MarkCompleted(string outputPath, string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        if (Status != PublishJobStatus.Running)
        {
            throw new InvalidOperationException($"Publish job can only move to Completed from Running. Current status: {Status}.");
        }

        Status = PublishJobStatus.Completed;
        OutputPath = outputPath.Trim();
        PackagePath = packagePath.Trim();
        CompletedUtc = DateTime.UtcNow;
        FailureReason = null;
        ClearLease();
    }

    public void MarkFailed(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        if (Status is PublishJobStatus.Completed or PublishJobStatus.Failed)
        {
            throw new InvalidOperationException($"Publish job in status {Status} cannot be marked as Failed.");
        }

        Status = PublishJobStatus.Failed;
        FailureReason = failureReason.Trim();
        PackagePath = null;
        CompletedUtc = DateTime.UtcNow;
        ClearLease();
    }

    public static PublishJob Rehydrate(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        PublishJobStatus status,
        string? outputPath,
        string? packagePath,
        DateTime createdUtc,
        DateTime? completedUtc,
        string? failureReason,
        string? idempotencyKey = null,
        int attemptCount = 0,
        DateTime? nextAttemptUtc = null,
        string? leaseOwner = null,
        Guid? leaseToken = null,
        DateTime? leaseExpiresUtc = null,
        DateTime? lastHeartbeatUtc = null)
    {
        return new PublishJob(
            id,
            applicationId,
            sequenceNumber,
            status,
            outputPath,
            packagePath,
            createdUtc,
            completedUtc,
            failureReason,
            idempotencyKey,
            attemptCount,
            nextAttemptUtc,
            leaseOwner,
            leaseToken,
            leaseExpiresUtc,
            lastHeartbeatUtc);
    }

    private void EnsureLease(Guid leaseToken, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (Status != PublishJobStatus.Running
            || LeaseToken != leaseToken
            || !string.Equals(LeaseOwner, owner.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Publish job {Id} is not owned by the supplied lease.");
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseToken = null;
        LeaseExpiresUtc = null;
        LastHeartbeatUtc = null;
    }

    private static string NormalizeIdempotencyKey(string? idempotencyKey, Guid id)
    {
        var normalized = string.IsNullOrWhiteSpace(idempotencyKey)
            ? id.ToString("N")
            : idempotencyKey.Trim();

        if (normalized.Length > 128 || normalized.Any(character => character is < '!' or > '~'))
        {
            throw new ArgumentException("Idempotency key must contain 1-128 visible ASCII characters.", nameof(idempotencyKey));
        }

        return normalized;
    }
}
