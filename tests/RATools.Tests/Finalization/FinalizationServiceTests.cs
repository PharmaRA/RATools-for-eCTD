using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

public sealed class FinalizationServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FinalizeBindsTheExactValidatedReportAndNeverReinterpretsReadiness()
    {
        await using var fixture = new Fixture();
        var (run, report) = await fixture.CompletedRunAsync(PackageValidationMode.Development);
        var result = await fixture.Service.FinalizeAsync(fixture.Request(run, report, PackageValidationMode.Development));
        Assert.True(result.Created);
        Assert.Equal(run.Id, result.Sequence.ValidationRunId);
        Assert.Equal(run.Binding.InputManifestDigest, result.Sequence.ValidationInputManifestDigest);
        Assert.Equal(run.ReportSha256, result.Sequence.ValidationReportSha256);
        Assert.Equal(report.Binding.HistoryBaselineDigest, result.Sequence.HistoryBaselineDigest);
        Assert.False(result.Sequence.IsReadyForFinalization);
        Assert.Contains(result.Sequence.BlockingReasons, reason => reason.StartsWith("DevelopmentMode", StringComparison.Ordinal));
        Assert.Contains(result.Sequence.BlockingReasons, reason => reason.StartsWith("ProfileNotQualified", StringComparison.Ordinal));
        // Delivery facts require a formal, ready snapshot: a development snapshot is an
        // archive of what was validated, not authorization to deliver.
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            fixture.Service.RegisterDeliveryAsync(result.Sequence.Id, Digest("package"), "gateway", null, null, "operator"));
    }

    [Fact]
    public async Task FinalizeRejectsAManifestOrBaselineTheRunDidNotValidate()
    {
        await using var fixture = new Fixture();
        var (run, report) = await fixture.CompletedRunAsync(PackageValidationMode.Development);
        var otherManifest = fixture.Request(run, report, PackageValidationMode.Development) with
        {
            ManifestDigest = new string('f', 64)
        };
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.FinalizeAsync(otherManifest));
        var otherBaseline = fixture.Request(run, report, PackageValidationMode.Development) with
        {
            HistoryBaselineDigest = new string('e', 64)
        };
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.FinalizeAsync(otherBaseline));
        var otherProfile = fixture.Request(run, report, PackageValidationMode.Development) with
        {
            StandardsProfileSnapshotId = PackageValidationCatalog.IchProfile
        };
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.FinalizeAsync(otherProfile));
        // A sequence that does not exist on the application is not a finalization target.
        var otherSequence = fixture.Request(run, report, PackageValidationMode.Development) with
        {
            SequenceNumber = "0002"
        };
        await Assert.ThrowsAsync<FinalizationNotFoundException>(() => fixture.Service.FinalizeAsync(otherSequence));
        Assert.Empty(await fixture.FinalizationStore.ListBySequenceAsync(run.ApplicationId, run.SequenceNumber));
    }

    [Fact]
    public async Task FinalizeRejectsIncompleteRunsAndChangedWorkspaceRevisions()
    {
        await using var fixture = new Fixture();
        var (run, report) = await fixture.CompletedRunAsync(PackageValidationMode.Development, complete: false);
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            fixture.Service.FinalizeAsync(fixture.Request(run, report, PackageValidationMode.Development)));

        var (completed, completedReport) = await fixture.CompletedRunAsync(PackageValidationMode.Development);
        // The request names the revision the operator saw; a later workspace change makes
        // that fixed input stale instead of silently finalizing newer content.
        var stale = fixture.Request(completed, completedReport, PackageValidationMode.Development);
        fixture.Application.AdvanceRevision();
        await Assert.ThrowsAsync<RATools.Application.Abstractions.Persistence.WorkspaceRevisionConflictException>(() =>
            fixture.Service.FinalizeAsync(stale));
    }

    [Fact]
    public async Task IdempotentReplayReturnsTheCommittedSnapshotAndChangedRequestsConflict()
    {
        await using var fixture = new Fixture();
        var (run, report) = await fixture.CompletedRunAsync(PackageValidationMode.Development);
        var request = fixture.Request(run, report, PackageValidationMode.Development) with { IdempotencyKey = "finalize-1" };
        var first = await fixture.Service.FinalizeAsync(request);
        var replay = await fixture.Service.FinalizeAsync(request);
        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Sequence.Id, replay.Sequence.Id);
        // The same content cannot be finalized twice under a new key, and a changed
        // request must not silently reuse the earlier snapshot.
        await Assert.ThrowsAsync<FinalizationSnapshotExistsException>(() => fixture.Service.FinalizeAsync(
            request with { IdempotencyKey = "finalize-2" }));
        var (otherRun, otherReport) = await fixture.CompletedRunAsync(PackageValidationMode.Development);
        await Assert.ThrowsAsync<FinalizationIdempotencyConflictException>(() => fixture.Service.FinalizeAsync(
            fixture.Request(otherRun, otherReport, PackageValidationMode.Development) with { IdempotencyKey = "finalize-1" }));
    }

    [Fact]
    public async Task FormalFinalizationRequiresAReadyReportAndNoCurrentProfileIsReady()
    {
        await using var fixture = new Fixture();
        var (run, report) = await fixture.CompletedRunAsync(PackageValidationMode.Formal);
        // The shipped catalog has no qualified profile, so a formal report always carries
        // blocking reasons. Finalization must refuse it instead of recording a ready
        // snapshot from an unready report.
        Assert.False(report.IsReadyForFinalization);
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            fixture.Service.FinalizeAsync(fixture.Request(run, report, PackageValidationMode.Formal)));
        Assert.Empty(await fixture.FinalizationStore.ListBySequenceAsync(run.ApplicationId, run.SequenceNumber));
    }

    [Fact]
    public async Task DeliveryAndReceiptsAreAppendOnlyAndSurviveReload()
    {
        await using var fixture = new Fixture();
        // A ready formal snapshot can only be produced once a qualified profile exists.
        // This fixture registers one directly to exercise the append-only delivery
        // contract; the service itself only creates snapshots from an archived report.
        var snapshot = FinalizedSequence.Create(fixture.Application.Id, "0001", fixture.Application.Revision,
            Digest("manifest"), PackageValidationCatalog.UsProfile, "3.2.2", "3.3", Digest("history"), Guid.NewGuid(),
            Digest("input"), Digest("report"), PackageValidationMode.Formal, isReadyForFinalization: true, [],
            FinalizationService.ToolVersion, "ready-snapshot", DateTime.UtcNow);
        await fixture.FinalizationStore.RegisterAsync(snapshot);
        var delivery = await fixture.Service.RegisterDeliveryAsync(snapshot.Id, Digest("package"), "gateway",
            "EXT-1", "Exported and handed to the gateway.", "operator");
        var transmission = await fixture.Service.RegisterReceiptAsync(delivery.Id, DeliveryReceiptKind.Transmission,
            "Accepted", "<ack />", "EXT-1", "operator");
        await fixture.Service.RegisterReceiptAsync(delivery.Id, DeliveryReceiptKind.BusinessOutcome,
            "Pending", "<pending />", "EXT-1", "operator");
        Assert.Equal(2, (await fixture.FinalizationStore.ListReceiptsAsync(delivery.Id)).Count);
        var deliveries = await fixture.FinalizationStore.ListDeliveriesAsync(snapshot.Id);
        Assert.Equal(delivery.Digest, Assert.Single(deliveries).Digest);
        Assert.Equal(DeliveryReceiptKind.Transmission, transmission.Kind);
        Assert.NotEmpty(await fixture.Service.ListAsync(snapshot.ApplicationId, snapshot.SequenceNumber));
        // Appends never rewrite the snapshot identity.
        var reloaded = await fixture.Service.GetAsync(snapshot.Id);
        Assert.Equal(snapshot.RequestDigest, reloaded.RequestDigest);
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    private sealed class Fixture : IAsyncDisposable
    {
        public InMemoryPackageValidationStore PackageStore { get; } = new();
        public InMemoryFinalizationStore FinalizationStore { get; } = new();
        public ApplicationRevision Application { get; } = new();
        public IWorkspaceRevisionStore Revisions => Application;
        public FinalizationService Service { get; }
        private readonly IPackageValidationStore _packageStore;

        public Fixture()
        {
            _packageStore = PackageStore;
            Service = new FinalizationService(FinalizationStore, _packageStore, Application);
        }

        public async Task<(PackageValidationRun Run, ValidationReport Report)> CompletedRunAsync(
            PackageValidationMode mode, bool complete = true)
        {
            var binding = new ValidationBinding(Digest("input"), Digest("history"), PackageValidationCatalog.UsProfile,
                PackageValidationCatalog.Current.Digest, "package-delivery-v3", mode, new PackageReadLimits().Digest());
            var run = new PackageValidationRun(Application.Id, "0001", binding, "{\"inputId\":\"fixture\"}",
                Guid.NewGuid().ToString("N"), Now);
            await PackageStore.EnqueueAsync(run);
            var lease = (await PackageStore.ClaimAsync("worker", Now, TimeSpan.FromMinutes(1)))!;
            var report = ValidationReport.Create(PackageValidationCatalog.Current, binding,
                complete ? ValidationRunStatus.Completed : ValidationRunStatus.Failed,
                [new RuleCheckResult("FILE-HREF", CheckStatus.Pass, "All referenced payloads exist.")]);
            if (!complete)
            {
                await PackageStore.FailAsync(run.Id, lease.Token, lease.Owner, Now, ValidationRunStatus.Failed,
                    "TOOL_ERROR", "Parser failed.");
                return (run, report);
            }
            Assert.True(await PackageStore.CompleteAsync(run.Id, lease.Token, lease.Owner, Now, report));
            return ((await PackageStore.GetRunAsync(run.Id))!, report);
        }

        public FinalizeSequenceRequest Request(PackageValidationRun run, ValidationReport report, PackageValidationMode mode) =>
            new(run.ApplicationId, run.SequenceNumber, Application.Revision, run.Id, run.Binding.InputManifestDigest,
                run.Binding.ProfileSnapshotId, "3.2.2", "3.3", run.Binding.HistoryBaselineDigest,
                Guid.NewGuid().ToString("N"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // The workspace revision is the only mutable input to finalization; the fixture keeps
    // it exact so a finalized snapshot cannot be created against a later revision.
    private sealed class ApplicationRevision : IWorkspaceRevisionStore
    {
        public Guid Id { get; } = Guid.NewGuid();
        public long Revision { get; private set; } = 3;
        public Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(applicationId == Id && sequenceNumber == "0001" ? Revision : null);
        public Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(new Noop());
        public Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            if (applicationId != Id || sequenceNumber != "0001" || Revision != expectedRevision) return Task.FromResult(false);
            Revision++;
            return Task.FromResult(true);
        }
        public void AdvanceRevision() => Revision++;
        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
