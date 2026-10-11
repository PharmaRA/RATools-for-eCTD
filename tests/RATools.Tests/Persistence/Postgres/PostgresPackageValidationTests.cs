using Microsoft.EntityFrameworkCore;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.PackageValidation;
using Microsoft.Extensions.Options;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresPackageValidationTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task ExternalInputAndRunSurviveIndependentRepositoryAndServiceInstances()
    {
        await using var files = new PackageFileInspectorTests.Fixture();
        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [files.PathOf("")] }));
        var settings = Options.Create(new PackageValidationOptions { StorageRoot = files.ArchivePath("durable-validation") });
        var reader = new PackageInputReader(Path.Combine(settings.Value.StorageRoot, "captures"));
        var inspector = new PackageDeliveryInspector(new PdfPigPackageInspector());
        Guid inputId;
        await using (var registrationContext = fixture.CreateDbContext())
        {
            var registry = new PackageValidationInputRegistry(new EfCorePackageValidationStore(registrationContext), reader, policy, settings);
            inputId = (await registry.RegisterAsync(new(Guid.NewGuid(), files.PathOf("0000")))).Id;
        }
        Guid runId;
        await using (var queueContext = fixture.CreateDbContext())
        {
            var service = new PackageValidationService(new EfCorePackageValidationStore(queueContext), reader, policy, inspector);
            runId = (await service.EnqueueAsync(new(inputId, PackageValidationCatalog.UsProfile, PackageValidationMode.Development, Guid.NewGuid().ToString("N")))).Run.Id;
        }
        await using (var executionContext = fixture.CreateDbContext())
        {
            var store = new EfCorePackageValidationStore(executionContext);
            var service = new PackageValidationService(store, reader, policy, inspector);
            var lease = (await store.ClaimAsync("durable-worker", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!;
            Assert.Equal(runId, lease.Run.Id);
            var report = await service.InspectAsync(lease.Run);
            Assert.True(await store.CompleteAsync(runId, lease.Token, lease.Owner, DateTime.UtcNow, report));
        }
        File.Delete(files.PathOf("0000/index.xml"));
        await using var verify = fixture.CreateDbContext();
        var persisted = new EfCorePackageValidationStore(verify);
        Assert.NotNull(await persisted.GetReportJsonAsync(runId));
        Assert.NotNull((await persisted.GetRunAsync(runId))!.CoverageSummary);
    }

    [RequiresPostgresFact]
    public async Task IndependentStorePersistsFindingsAndDoesNotRequireApplicationRows()
    {
        await using var context = fixture.CreateDbContext();
        await PackageValidationStoreTests.ExerciseAsync(new EfCorePackageValidationStore(context));
    }

    [RequiresPostgresFact]
    public async Task ConcurrentEnqueueClaimAndCancellationFenceArchiveCommit()
    {
        var run = PackageValidationRunTests.NewRun();
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var a = new EfCorePackageValidationStore(first);
        var b = new EfCorePackageValidationStore(second);
        var results = await Task.WhenAll(a.EnqueueAsync(run), b.EnqueueAsync(PackageValidationRunTests.NewRun(run.IdempotencyKey, run.ApplicationId)));
        Assert.Single(results, result => result.Created);
        run = results.Single(result => result.Created).Run;
        Assert.All(results, result => Assert.Equal(run.Id, result.Run.Id));
        var now = PackageValidationRunTests.Now;
        var claims = await Task.WhenAll(a.ClaimAsync("worker-a", now, TimeSpan.FromMinutes(1)), b.ClaimAsync("worker-b", now, TimeSpan.FromMinutes(1)));
        var lease = Assert.Single(claims, claim => claim is not null)!;
        await b.CancelAsync(run.Id, now.AddSeconds(1));
        Assert.False(await a.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(2), PackageValidationRunTests.Report(run.Binding)));
        Assert.Null(await b.GetReportJsonAsync(run.Id));
        var recoveries = await Task.WhenAll(a.RecoverExpiredAsync(now.AddMinutes(2), 3), b.RecoverExpiredAsync(now.AddMinutes(2), 3));
        Assert.Equal(1, recoveries.Sum());
        Assert.Equal(ValidationRunStatus.Cancelled, (await b.GetRunAsync(run.Id))!.Status);
    }

    [RequiresPostgresFact]
    public async Task ArchivedReportDigestRejectsCorruptionAndTerminalRowsRejectResurrection()
    {
        await using var context = fixture.CreateDbContext();
        var store = new EfCorePackageValidationStore(context);
        var run = PackageValidationRunTests.NewRun();
        await store.EnqueueAsync(run);
        var now = PackageValidationRunTests.Now;
        var lease = (await store.ClaimAsync("worker", now, TimeSpan.FromMinutes(1)))!;
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now, PackageValidationRunTests.Report(run.Binding)));
        await context.Set<PackageValidationReportRecord>().Where(row => row.RunId == run.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Json, "{}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetReportJsonAsync(run.Id));
        Assert.False(await store.HeartbeatAsync(run.Id, lease.Token, lease.Owner, now, TimeSpan.FromMinutes(1), "Inspecting"));
        Assert.Equal(ValidationRunStatus.Completed, (await store.GetRunAsync(run.Id))!.Status);
        await context.Set<PackageValidationRun>().Where(row => row.Id == run.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestJson, "{\"inputId\":\"changed\"}"));
        var corrupted = (await store.GetRunAsync(run.Id))!;
        Assert.Throws<InvalidDataException>(corrupted.VerifyRequestIntegrity);
    }

    [RequiresPostgresFact]
    public async Task CancellationRacingArchiveCommitsOneConsistentOutcome()
    {
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var a = new EfCorePackageValidationStore(first);
        var b = new EfCorePackageValidationStore(second);
        var run = PackageValidationRunTests.NewRun();
        await a.EnqueueAsync(run);
        var now = PackageValidationRunTests.Now;
        var lease = (await a.ClaimAsync("worker", now, TimeSpan.FromMinutes(1)))!;
        var completion = a.CompleteAsync(run.Id, lease.Token, lease.Owner, now, PackageValidationRunTests.Report(run.Binding));
        var cancellation = b.CancelAsync(run.Id, now);
        await Task.WhenAll(completion, cancellation);
        if (!await completion) await a.FailAsync(run.Id, lease.Token, lease.Owner, now, ValidationRunStatus.Cancelled, "CANCELLED", "Cancelled.");
        var final = (await a.GetRunAsync(run.Id))!;
        Assert.Equal(await completion ? ValidationRunStatus.Completed : ValidationRunStatus.Cancelled, final.Status);
        Assert.Equal(await completion, await a.GetReportJsonAsync(run.Id) is not null);
    }

    [RequiresPostgresFact]
    public async Task NewRepositoryRecoversExpiredAttemptsWithoutReusingOldWorkerToken()
    {
        var now = PackageValidationRunTests.Now;
        var run = PackageValidationRunTests.NewRun();
        Guid token;
        await using (var oldContext = fixture.CreateDbContext())
        {
            var old = new EfCorePackageValidationStore(oldContext);
            await old.EnqueueAsync(run);
            token = (await old.ClaimAsync("old", now, TimeSpan.FromSeconds(1)))!.Token;
        }
        await using var context = fixture.CreateDbContext();
        var current = new EfCorePackageValidationStore(context);
        Assert.Equal(1, await current.RecoverExpiredAsync(now.AddSeconds(2), 2));
        var retried = (await current.ClaimAsync("new", now.AddSeconds(2), TimeSpan.FromSeconds(5)))!;
        Assert.Equal(2, retried.Run.AttemptCount);
        Assert.Equal(run.RequestDigest, retried.Run.RequestDigest);
        Assert.False(await current.CompleteAsync(run.Id, token, "old", now.AddSeconds(3), PackageValidationRunTests.Report(run.Binding)));
        Assert.True(await current.CompleteAsync(run.Id, retried.Token, retried.Owner, now.AddSeconds(3), PackageValidationRunTests.Report(run.Binding)));
    }
}
