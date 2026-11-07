using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.PackageValidation;

public sealed class PackageValidationServiceTests
{
    [Fact]
    public async Task FreshAttemptsBindSelectedHistoryAndPreserveArchivedEvidenceAfterSourceRemoval()
    {
        await using var fixture = new ServiceFixture();
        var zero = await fixture.Register("0000");
        var one = await fixture.Register("0001");
        var request = new CreatePackageValidationRequest(one.Id, PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "history",
            History: [new(zero.Id, PackageValidationCatalog.UsProfile)]);
        var enqueued = await fixture.Service.EnqueueAsync(request);
        var lease = (await fixture.Store.ClaimAsync("worker", DateTime.UtcNow, TimeSpan.FromSeconds(10)))!;
        DossierProjection? projection = null;
        var report = await fixture.Service.InspectAsync(lease.Run, null,
            (DossierProjection value) => { projection = value; return Task.CompletedTask; });
        Assert.Equal(enqueued.Run.Binding, report.Binding);
        Assert.NotNull(projection);
        Assert.Equal(report.Binding.InputManifestDigest, projection!.InputDigest);
        // Selected history is bound but its trust is unverified, so the projection must
        // report itself incomplete rather than authorize a cumulative claim.
        Assert.False(projection.IsComplete);
        Assert.Contains(projection.IncompleteReasons, reason => reason.Contains("Lifecycle resolution", StringComparison.Ordinal));
        Assert.Contains(projection.EffectiveLeaves, leaf => leaf.Address.SequenceNumber == "0001");
        Assert.Contains(report.Coverage, rule => rule.RuleId == "HISTORY-BASELINE" && rule.CheckStatus == CheckStatus.NotEvaluated);
        Assert.True(await fixture.Store.CompleteAsync(lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow, report));
        fixture.Files.RetainMissingFile(fixture.Files.PathOf("0001/index.xml"));
        Assert.NotNull(await fixture.Store.GetReportJsonAsync(lease.Run.Id));
        Assert.False((await fixture.Service.EnqueueAsync(request)).Created);
        await Assert.ThrowsAsync<PackageValidationIdempotencyConflictException>(() => fixture.Service.EnqueueAsync(request with { History = [] }));
    }

    [Fact]
    public async Task InputChangedIsTerminalAndCannotReuseARegistrationForDifferentBytes()
    {
        await using var fixture = new ServiceFixture();
        var input = await fixture.Register("0000");
        var run = (await fixture.Service.EnqueueAsync(new(input.Id, PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "changed"))).Run;
        File.AppendAllText(fixture.Files.PathOf("0000/m2/25-clin-over/overview.pdf"), "changed");
        var lease = (await fixture.Store.ClaimAsync("worker", DateTime.UtcNow, TimeSpan.FromSeconds(10)))!;
        await fixture.Worker.ProcessAsync(lease, CancellationToken.None);
        Assert.Equal(ValidationRunStatus.InputChanged, (await fixture.Store.GetRunAsync(run.Id))!.Status);
        Assert.Null(await fixture.Store.GetReportJsonAsync(run.Id));
        var different = await fixture.Register("0000");
        Assert.NotEqual(input.InputDigest, different.InputDigest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndTimeoutAreQueryableWhileParserCooperates(bool timeout)
    {
        var gate = new GateInspector();
        await using var fixture = new ServiceFixture(gate, timeout ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(10));
        var input = await fixture.Register("0000");
        var run = (await fixture.Service.EnqueueAsync(new(input.Id, PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "cancel"))).Run;
        var lease = (await fixture.Store.ClaimAsync("worker", DateTime.UtcNow, TimeSpan.FromSeconds(10)))!;
        var processing = fixture.Worker.ProcessAsync(lease, CancellationToken.None);
        try
        {
            if (!timeout)
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await fixture.Store.CancelAsync(run.Id, DateTime.UtcNow);
            }
            await processing.WaitAsync(TimeSpan.FromSeconds(10));
            var ended = (await fixture.Store.GetRunAsync(run.Id))!;
            Assert.Equal(timeout ? ValidationRunStatus.Failed : ValidationRunStatus.Cancelled, ended.Status);
            Assert.Equal(timeout ? "VALIDATION_TIMEOUT" : "CANCELLED", ended.FailureCode);
            Assert.Null(await fixture.Store.GetReportJsonAsync(run.Id));
        }
        finally { gate.Release.Set(); await processing; }
    }

    [Fact]
    public async Task NonCooperativeParserKeepsCapturesAliveAfterCancellationBecomesTerminal()
    {
        var gate = new GateInspector(ignoreCancellation: true);
        await using var fixture = new ServiceFixture(gate);
        var input = await fixture.Register("0000");
        var run = (await fixture.Service.EnqueueAsync(new(input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "noncooperative"))).Run;
        var lease = (await fixture.Store.ClaimAsync("worker", DateTime.UtcNow, TimeSpan.FromSeconds(10)))!;
        var processing = fixture.Worker.ProcessAsync(lease, CancellationToken.None);
        string[]? capturedFingerprints = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Store.CancelAsync(run.Id, DateTime.UtcNow);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((await fixture.Store.GetRunAsync(run.Id))!.Status == ValidationRunStatus.Running)
                await Task.Delay(25, deadline.Token);
            Assert.Equal(ValidationRunStatus.Cancelled, (await fixture.Store.GetRunAsync(run.Id))!.Status);
            Assert.False(processing.IsCompleted);
            Assert.Null(await fixture.Store.GetReportJsonAsync(run.Id));
            capturedFingerprints = RetainedPackageTestFiles.CaptureFingerprints(Path.Combine(fixture.Storage, "captures"));
            Assert.NotEmpty(capturedFingerprints);
        }
        finally { gate.Release.Set(); await processing.WaitAsync(TimeSpan.FromSeconds(10)); }
        RetainedPackageTestFiles.AssertOnlyRecycledCaptures(Path.Combine(fixture.Storage, "captures"));
        Assert.NotNull(capturedFingerprints);
        Assert.Equal(capturedFingerprints, RetainedPackageTestFiles.CaptureFingerprints(Path.Combine(fixture.Storage, "captures")));
    }

    [Fact]
    public async Task HostShutdownLeavesRecoverableLeaseAndRestartUsesFreshSources()
    {
        var gate = new GateInspector();
        await using var fixture = new ServiceFixture(gate);
        var input = await fixture.Register("0000");
        var run = (await fixture.Service.EnqueueAsync(new(input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "shutdown"))).Run;
        var now = DateTime.UtcNow;
        var lease = (await fixture.Store.ClaimAsync("worker", now, TimeSpan.FromSeconds(10)))!;
        using var shutdown = new CancellationTokenSource();
        var processing = fixture.Worker.ProcessAsync(lease, shutdown.Token);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            shutdown.Cancel();
            await processing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ValidationRunStatus.Running, (await fixture.Store.GetRunAsync(run.Id))!.Status);
            Assert.Null(await fixture.Store.GetReportJsonAsync(run.Id));
            Assert.Equal(1, await fixture.Store.RecoverExpiredAsync(now.AddMinutes(1), 3));
            var retry = (await fixture.Store.ClaimAsync("replacement", now.AddMinutes(1), TimeSpan.FromSeconds(10)))!;
            Assert.Equal(2, retry.Run.AttemptCount);
            Assert.NotEqual(lease.Token, retry.Token);
            fixture.Files.RetainMissingFile(fixture.Files.PathOf("0000/index.xml"));
            await Assert.ThrowsAsync<PackageInputChangedException>(() => fixture.Service.InspectAsync(retry.Run));
        }
        finally { shutdown.Cancel(); gate.Release.Set(); await processing; }
    }

    [Fact]
    public async Task VersionChangeIsQueryableAndRequiresANewRun()
    {
        await using var fixture = new ServiceFixture();
        var input = await fixture.Register("0000");
        var run = (await fixture.Service.EnqueueAsync(new(input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "version-change"))).Run;
        // A queued run whose archived engine version no longer matches the current
        // inspector must fail with an explicit, queryable code instead of reusing
        // partial or stale observations.
        var stale = new PackageValidationRun(run.ApplicationId, run.SequenceNumber,
            new(run.Binding.InputManifestDigest, run.Binding.HistoryBaselineDigest, run.Binding.ProfileSnapshotId,
                run.Binding.RulesDigest, "package-delivery-v0", run.Binding.ValidationMode, run.Binding.ResourceLimitsDigest),
            run.RequestJson, "version-change-stale", DateTime.UtcNow);
        var enqueued = await fixture.Store.EnqueueAsync(stale);
        // Claim exactly this run; an earlier enqueue may still be pending in the shared
        // in-memory store and ClaimAsync would otherwise take the oldest row.
        var lease = await fixture.Store.ClaimAsync("worker", DateTime.UtcNow, TimeSpan.FromSeconds(10));
        Assert.NotNull(lease);
        while (lease!.Run.Id != enqueued.Run.Id)
        {
            await fixture.Store.FailAsync(lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
                ValidationRunStatus.Failed, "TEST_SKIP", "Skipped an unrelated queued run.");
            lease = await fixture.Store.ClaimAsync("worker", DateTime.UtcNow, TimeSpan.FromSeconds(10));
            Assert.NotNull(lease);
        }
        await fixture.Worker.ProcessAsync(lease, CancellationToken.None);
        var ended = (await fixture.Store.GetRunAsync(enqueued.Run.Id))!;
        Assert.Equal(ValidationRunStatus.Failed, ended.Status);
        Assert.Equal("VALIDATION_VERSION_CHANGED", ended.FailureCode);
        Assert.Null(await fixture.Store.GetReportJsonAsync(enqueued.Run.Id));
    }

    [Fact]
    public async Task DisabledWorkerLeavesQueuedRunsObservablyPending()
    {
        await using var fixture = new ServiceFixture(enabled: false);
        var input = await fixture.Register("0000");
        var run = (await fixture.Service.EnqueueAsync(new(input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "disabled-worker"))).Run;
        await fixture.Worker.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await fixture.Worker.StopAsync(CancellationToken.None);
        // The API still accepts work; without a worker the run remains Pending and
        // never fabricates a terminal result.
        Assert.Equal(ValidationRunStatus.Pending, (await fixture.Store.GetRunAsync(run.Id))!.Status);
        Assert.Null(await fixture.Store.GetReportJsonAsync(run.Id));
    }

    [Fact]
    public async Task RegistryRestrictsServerPathsAndFailedUploadCleansOnlyItsOwnedBytes()
    {
        await using var fixture = new ServiceFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Registry.RegisterAsync(new(Guid.NewGuid(), Path.GetPathRoot(fixture.Files.PathOf(""))!)));
        using var invalid = new MemoryStream("not a zip"u8.ToArray());
        await Assert.ThrowsAsync<PackageInputException>(() => fixture.Registry.UploadAsync(Guid.NewGuid(), invalid, "0000", null));
        var inputs = Path.Combine(fixture.Storage, "inputs");
        var recycle = Assert.Single(Directory.GetFileSystemEntries(inputs));
        Assert.Equal(Path.Combine(inputs, "_recycle"), recycle);
        var retained = Assert.Single(Directory.GetFileSystemEntries(recycle));
        var name = Path.GetFileName(retained);
        Assert.Equal(65, name.Length);
        Assert.True(Guid.TryParseExact(name[..32], "N", out _)); Assert.Equal('-', name[32]);
        Assert.True(Guid.TryParseExact(name[33..], "N", out _));
        var archive = Assert.Single(Directory.GetFileSystemEntries(retained));
        Assert.Equal("package.zip", Path.GetFileName(archive));
        Assert.Equal("not a zip"u8.ToArray(), await File.ReadAllBytesAsync(archive));
        Assert.True(File.Exists(fixture.Files.PathOf("0000/index.xml")));
    }

    private sealed class GateInspector(bool ignoreCancellation = false) : IPackagePdfInspector
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public string EngineVersion => "gated-test-pdf";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            Release.Wait(ignoreCancellation ? CancellationToken.None : cancellationToken);
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

    private sealed class ServiceFixture : IAsyncDisposable
    {
        public PackageFileInspectorTests.Fixture Files { get; } = new();
        public InMemoryPackageValidationStore Store { get; } = new();
        public PackageValidationInputRegistry Registry { get; }
        public PackageValidationService Service { get; }
        public PackageValidationBackgroundService Worker { get; }
        public string Storage => Files.ArchivePath("validation");
        private readonly ServiceProvider _provider;
        private readonly Guid _application = Guid.NewGuid();

        public ServiceFixture(IPackagePdfInspector? pdf = null, TimeSpan? timeout = null, bool enabled = true)
        {
            var options = Options.Create(new PackageValidationOptions { StorageRoot = Storage, Enabled = enabled,
                HeartbeatInterval = TimeSpan.FromMilliseconds(25), LeaseDuration = TimeSpan.FromSeconds(10), ExecutionTimeout = timeout ?? TimeSpan.FromSeconds(30) });
            var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [Files.PathOf("")] }));
            var reader = new PackageInputReader(Path.Combine(Storage, "captures"));
            var inspector = new PackageDeliveryInspector(pdf ?? new PdfPigPackageInspector());
            Registry = new(Store, reader, policy, options);
            Service = new(Store, reader, policy, inspector);
            _provider = new ServiceCollection().AddSingleton<IPackageValidationStore>(Store).AddSingleton(Service).BuildServiceProvider();
            Worker = new(_provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<PackageValidationBackgroundService>.Instance);
        }
        public Task<PackageInputDto> Register(string sequence) => Registry.RegisterAsync(new(_application, Files.PathOf(sequence)));
        public async ValueTask DisposeAsync() { Worker.Dispose(); await _provider.DisposeAsync(); await Files.DisposeAsync(); }
    }
}
