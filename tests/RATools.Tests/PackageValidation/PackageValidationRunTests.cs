using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageValidationRunTests
{
    internal static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    internal static ValidationBinding Binding(string? input = null) => new(input ?? new string('a', 64), new string('b', 64),
        PackageValidationCatalog.IchProfile, PackageValidationCatalog.Current.Digest, "test-delivery-v1", PackageValidationMode.Development, new PackageReadLimits().Digest());
    internal static PackageValidationRun NewRun(string? key = null, Guid? applicationId = null, ValidationBinding? binding = null,
        string request = "{\"inputId\":\"external-input\"}") => new(applicationId ?? Guid.NewGuid(), "0000", binding ?? Binding(), request, key ?? Guid.NewGuid().ToString("N"), Now);
    internal static ValidationReport Report(ValidationBinding binding) => ValidationReport.Create(PackageValidationCatalog.Current, binding,
        ValidationRunStatus.Completed, [new RuleCheckResult("FILE-HREF", CheckStatus.Fail, "Missing payloads", [
            new("FILE-HREF", CheckStatus.Fail, ValidationSeverity.Error, "MISSING_A", "A is missing.", new("0000", "0000/a.pdf")),
            new("FILE-HREF", CheckStatus.Fail, ValidationSeverity.Error, "MISSING_B", "B is missing.", new("0000", "0000/b.pdf"))])]);

    [Fact]
    public void ExactRequestDigestIncludesEveryBindingDimensionAndCanonicalRequest()
    {
        var app = Guid.NewGuid();
        var first = NewRun(applicationId: app, request: "{\"x\":1,\"y\":2}");
        var reordered = NewRun(applicationId: app, request: "{\"y\":2,\"x\":1}");
        Assert.Equal(first.RequestDigest, reordered.RequestDigest);
        Assert.NotEqual(first.RequestDigest, NewRun(applicationId: app, binding: Binding(new string('c', 64)), request: "{\"x\":1,\"y\":2}").RequestDigest);
        Assert.NotEqual(first.RequestDigest, NewRun(applicationId: app, request: "{\"x\":1,\"y\":3}").RequestDigest);
        Assert.Throws<ArgumentException>(() => NewRun(request: "{\"x\":1,\"x\":2}"));
    }

    [Fact]
    public void FindingsDoNotTurnSuccessfulExecutionIntoWorkerFailure()
    {
        var run = NewRun();
        var token = run.Claim("worker", Now, TimeSpan.FromMinutes(1));
        var report = Report(run.Binding);
        run.Complete(token, "worker", Now.AddSeconds(1), report, new string('d', 64));
        Assert.Equal(ValidationRunStatus.Completed, run.Status);
        Assert.False(report.IsReadyForFinalization);
        Assert.True(run.IsTerminal);
        Assert.False(run.RequestCancellation(Now.AddSeconds(2)));
        Assert.Null(run.LeaseToken);
        Assert.Equal(2, run.Revision);
    }

    [Fact]
    public void PendingCancellationIsTerminalAndRunningCancellationFencesCompletion()
    {
        var pending = NewRun();
        Assert.True(pending.RequestCancellation(Now));
        Assert.Equal(ValidationRunStatus.Cancelled, pending.Status);
        Assert.Throws<InvalidOperationException>(() => pending.Claim("worker", Now, TimeSpan.FromMinutes(1)));
        var running = NewRun();
        var token = running.Claim("worker", Now, TimeSpan.FromMinutes(1));
        Assert.True(running.RequestCancellation(Now.AddSeconds(1)));
        Assert.Equal(ValidationRunStatus.Running, running.Status);
        Assert.Throws<InvalidOperationException>(() => running.Complete(token, "worker", Now.AddSeconds(2), Report(running.Binding), new string('d', 64)));
        running.Fail(token, "worker", Now.AddSeconds(2), ValidationRunStatus.Failed, "TOOL_ERROR", "Parser failed.");
        Assert.Equal(ValidationRunStatus.Cancelled, running.Status);
        Assert.Null(running.ReportSha256);
    }

    [Fact]
    public void ExpiredWorkerCannotRenewOrWriteAfterRecoveryAndAttemptsAreBounded()
    {
        var run = NewRun();
        var old = run.Claim("old", Now, TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => run.Heartbeat(old, "old", Now.AddSeconds(5), TimeSpan.FromMinutes(1), "Inspecting"));
        Assert.True(run.RecoverExpiredLease(Now.AddSeconds(5), 2));
        Assert.Equal(ValidationRunStatus.Pending, run.Status);
        var current = run.Claim("new", Now.AddSeconds(6), TimeSpan.FromSeconds(5));
        Assert.NotEqual(old, current);
        Assert.False(run.OwnsLease(old, "old", Now.AddSeconds(7)));
        Assert.True(run.RecoverExpiredLease(Now.AddSeconds(11), 2));
        Assert.Equal(ValidationRunStatus.Failed, run.Status);
        Assert.Equal("WORKER_LEASE_EXPIRED", run.FailureCode);
        Assert.Equal(2, run.AttemptCount);
    }

    [Fact]
    public void RecoveryAcknowledgesCancellationAndMismatchedReportsCannotCommit()
    {
        var run = NewRun();
        var token = run.Claim("worker", Now, TimeSpan.FromSeconds(5));
        Assert.Throws<ArgumentException>(() => run.Complete(token, "worker", Now.AddSeconds(1), Report(Binding(new string('e', 64))), new string('d', 64)));
        Assert.Equal(ValidationRunStatus.Running, run.Status);
        run.RequestCancellation(Now.AddSeconds(2));
        Assert.True(run.RecoverExpiredLease(Now.AddSeconds(5), 3));
        Assert.Equal(ValidationRunStatus.Cancelled, run.Status);
        Assert.False(run.RecoverExpiredLease(Now.AddSeconds(6), 3));
    }
}
