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
        var projection = DossierProjection.Create(run.ApplicationId, "0000", PackageValidationCatalog.IchProfile,
            run.Binding.InputManifestDigest, run.Binding.HistoryBaselineDigest, run.Binding.ResourceLimitsDigest, "projection-test-v1",
            true, [], new HistoryBaselineManifest(run.ApplicationId, "0000", []), [], [], [], ["0000"]);
        Assert.False(await store.CompleteAsync(run.Id, Guid.NewGuid(), "test-worker", now, report, projection));
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(1), report, projection));
        Assert.False(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(2), report, projection));
        // The projection is archived with the same commit and bound to the report.
        using (var archived = JsonDocument.Parse((await store.GetProjectionJsonAsync(run.Id))!))
            Assert.Equal("0000", archived.RootElement.GetProperty("targetSequence").GetString());
        var mismatched = DossierProjection.Create(run.ApplicationId, "0000", PackageValidationCatalog.IchProfile,
            new string('c', 64), run.Binding.HistoryBaselineDigest, run.Binding.ResourceLimitsDigest, "projection-test-v1",
            true, [], new HistoryBaselineManifest(run.ApplicationId, "0000", []), [], [], [], ["0000"]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(3), report, mismatched));
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

        var evidence = new ExternalComparisonReport(run.Id, run.Binding, "eValidator", "22.1", run.Binding.InputManifestDigest,
            "<external-report />", [new("EXT-1", "FILE-HREF", "Missing payload", "Both tools resolve the same absent file.")], now);
        Assert.True(await store.RegisterExternalComparisonAsync(evidence));
        Assert.False(await store.RegisterExternalComparisonAsync(evidence));
        var stored = Assert.IsType<ExternalComparisonEvidence>(await store.GetExternalComparisonAsync(run.Id));
        Assert.Equal(evidence.Digest, stored.Digest);
        Assert.Equal("<external-report />", await store.GetExternalComparisonReportTextAsync(run.Id));
        Assert.Null(await store.GetExternalComparisonAsync(Guid.NewGuid()));
        var conflicting = new ExternalComparisonReport(run.Id, run.Binding, "eValidator", "22.1", run.Binding.InputManifestDigest,
            "<external-report />", [new("EXT-1", "FILE-HREF", "Missing payload", "A different review conclusion.")], now);
        await Assert.ThrowsAsync<ExternalComparisonEvidenceConflictException>(() => store.RegisterExternalComparisonAsync(conflicting));
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
