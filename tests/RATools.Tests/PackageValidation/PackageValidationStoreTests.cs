using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class PackageValidationStoreTests
{
    [Fact]
    public Task InMemoryProviderUsesTheSameRunContract() => ExerciseAsync(new InMemoryPackageValidationStore());

    internal static async Task ExerciseAsync(IPackageValidationStore store)
    {
        const string request = """{"ruleCatalog":{"rules":[{"internalRuleId":"FILE-HREF","category":"ArchivedFiles"}]}}""";
        var run = PackageValidationRunTests.NewRun(request: request);
        var created = await store.EnqueueAsync(run);
        var duplicate = await store.EnqueueAsync(PackageValidationRunTests.NewRun(run.IdempotencyKey, run.ApplicationId, request: request));
        Assert.True(created.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(run.Id, duplicate.Run.Id);
        await Assert.ThrowsAsync<PackageValidationIdempotencyConflictException>(() => store.EnqueueAsync(
            PackageValidationRunTests.NewRun(run.IdempotencyKey, run.ApplicationId, PackageValidationRunTests.Binding(new string('e', 64)))));

        var now = PackageValidationRunTests.Now;
        var lease = Assert.IsType<PackageValidationLease>(await store.ClaimAsync("test-worker", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(run.Id, lease.Run.Id);
        Assert.Null(await store.ClaimAsync("second-worker", now, TimeSpan.FromMinutes(1)));
        var report = PackageValidationRunTests.Report(run.Binding);
        Assert.False(await store.CompleteAsync(run.Id, Guid.NewGuid(), "test-worker", now, report));
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(1), report));
        Assert.False(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(2), report));
        var persisted = Assert.IsType<PackageValidationRun>(await store.GetRunAsync(run.Id));
        Assert.Equal(ValidationRunStatus.Completed, persisted.Status);
        Assert.Equal(report.Summary, persisted.CoverageSummary);
        using var json = JsonDocument.Parse((await store.GetReportJsonAsync(run.Id))!);
        Assert.Equal("Completed", json.RootElement.GetProperty("runStatus").GetString());
        Assert.False(json.RootElement.GetProperty("isReadyForFinalization").GetBoolean());
        var page = await store.GetFindingsAsync(run.Id, new(Offset: 1, Limit: 1, Severity: ValidationSeverity.Error));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal("MISSING_B", Assert.Single(page.Items).Code);
        var path = await store.GetFindingsAsync(run.Id, new(LogicalPath: "0000/a.pdf"));
        Assert.Equal("MISSING_A", Assert.Single(path.Items).Code);
        Assert.Empty((await store.GetFindingsAsync(run.Id, new(Severity: ValidationSeverity.Info))).Items);
        Assert.Equal(2, (await store.GetFindingsAsync(run.Id, new(Category: "ArchivedFiles"))).TotalCount);
        Assert.Empty((await store.GetFindingsAsync(run.Id, new(Category: "Unknown"))).Items);
        Assert.Equal("MISSING_A", Assert.Single((await store.GetFindingsAsync(run.Id, new(Category: "ArchivedFiles", NodePath: "m3-2-s-4-1"))).Items).Code);
        Assert.Empty((await store.GetFindingsAsync(run.Id, new(NodePath: "M3-2-S"))).Items);
        Assert.Equal(run.Id, Assert.Single((await store.ListRunsAsync(run.ApplicationId, 0, 10)).Items).Id);
        Assert.Equal(ValidationRunStatus.Completed, (await store.CancelAsync(run.Id, now.AddSeconds(3)))!.Status);
        Assert.Equal(0, await store.RecoverExpiredAsync(now.AddMinutes(2), 3));
    }

    [Fact]
    public async Task RunningCancellationNeverPublishesAnArchiveAndRecoveryInvalidatesOldTokens()
    {
        var store = new InMemoryPackageValidationStore();
        var run = PackageValidationRunTests.NewRun();
        await store.EnqueueAsync(run);
        var now = PackageValidationRunTests.Now;
        var lease = (await store.ClaimAsync("worker", now, TimeSpan.FromSeconds(2)))!;
        await store.CancelAsync(run.Id, now.AddSeconds(1));
        Assert.False(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(1), PackageValidationRunTests.Report(run.Binding)));
        Assert.Equal(1, await store.RecoverExpiredAsync(now.AddSeconds(2), 3));
        Assert.Equal(ValidationRunStatus.Cancelled, (await store.GetRunAsync(run.Id))!.Status);
        Assert.Null(await store.GetReportJsonAsync(run.Id));
        Assert.False(await store.HeartbeatAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(3), TimeSpan.FromSeconds(2), "Inspecting"));
    }
}
