using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresPackageEvidenceIntegrityTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task FindingPagesRejectChangedContentFiltersAndOrdinalAgainstTheCommittedReport()
    {
        foreach (var field in new[] { "json", "rule", "severity", "status", "path", "node", "ordinal" })
        {
            await using var db = fixture.CreateDbContext();
            var (store, run, lease) = await RunningAsync(db);
            Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow, PackageValidationRunTests.Report(run.Binding)));
            var row = await db.Set<PackageValidationFindingRecord>().SingleAsync(item => item.RunId == run.Id && item.Ordinal == 0);
            switch (field)
            {
                case "json": row.Json = row.Json.Replace("MISSING_A", "FORGED_A", StringComparison.Ordinal); break;
                case "rule": row.RuleId = "FORGED"; break;
                case "severity": row.Severity = "Info"; break;
                case "status": row.Status = "Pass"; break;
                case "path": row.LogicalPath = "0000/else.pdf"; break;
                case "node": row.NodePath = "/changed"; break;
                case "ordinal":
                    // Keep the original evidence row and add a duplicate with a new ordinal.
                    db.Add(new PackageValidationFindingRecord { RunId = run.Id, Ordinal = 20, RuleId = row.RuleId,
                        Severity = row.Severity, Status = row.Status, LogicalPath = row.LogicalPath, NodePath = row.NodePath, Json = row.Json });
                    break;
            }
            await db.SaveChangesAsync();
            await using var read = fixture.CreateDbContext();
            await Assert.ThrowsAsync<InvalidDataException>(() => new EfCorePackageValidationStore(read).GetFindingsAsync(run.Id,
                new(Limit: 1, RuleId: "FILE-HREF", Severity: ValidationSeverity.Error, Status: CheckStatus.Fail,
                    LogicalPath: "0000/a.pdf", NodePath: "m3-2-s-4-1")));
        }
    }

    [RequiresPostgresFact]
    public async Task FindingPagesCannotServeRowsFromACorruptedArchivedReport()
    {
        await using var db = fixture.CreateDbContext();
        var (store, run, lease) = await RunningAsync(db);
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow, PackageValidationRunTests.Report(run.Binding)));
        await db.Set<PackageValidationReportRecord>().Where(row => row.RunId == run.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Json, "{}"));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetFindingsAsync(run.Id, new()));
    }

    [RequiresPostgresFact]
    public async Task ProjectionReadsRejectWrongRunIdentityEvenWhenTheStoredHashWasRecomputed()
    {
        foreach (var field in new[] { "applicationId", "targetSequence", "inputDigest", "baselineDigest" })
        {
            await using var db = fixture.CreateDbContext();
            var (store, run, lease) = await RunningAsync(db);
            Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
                PackageValidationRunTests.Report(run.Binding), Projection(run)));
            var row = await db.Set<PackageValidationProjectionRecord>().SingleAsync(item => item.RunId == run.Id);
            var json = JsonNode.Parse(row.Json)!.AsObject();
            json[field] = field == "applicationId" ? Guid.NewGuid().ToString("D") : field == "targetSequence" ? "0002" : new string('e', 64);
            row.Json = json.ToJsonString();
            row.Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Json))).ToLowerInvariant();
            await db.SaveChangesAsync();
            await using var read = fixture.CreateDbContext();

            await Assert.ThrowsAsync<InvalidDataException>(() => new EfCorePackageValidationStore(read).GetProjectionJsonAsync(run.Id));
        }
    }

    [RequiresPostgresFact]
    public async Task ProjectionCompletionRejectsAnotherApplicationOrSequenceBeforeArchiving()
    {
        foreach (var otherApplication in new[] { true, false })
        {
            await using var db = fixture.CreateDbContext();
            var (store, run, lease) = await RunningAsync(db);
            var app = otherApplication ? Guid.NewGuid() : run.ApplicationId;
            var sequence = otherApplication ? run.SequenceNumber : "0002";
            var projection = DossierProjection.Create(app, sequence, run.Binding.ProfileSnapshotId,
                run.Binding.InputManifestDigest, run.Binding.HistoryBaselineDigest, run.Binding.ResourceLimitsDigest, "test-v1", true,
                [], new HistoryBaselineManifest(app, sequence, []), [], [], [], [sequence]);
            try
            {
                await Assert.ThrowsAsync<ArgumentException>(() => store.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
                    PackageValidationRunTests.Report(run.Binding), projection));
                Assert.Null(await store.GetReportJsonAsync(run.Id));
            }
            finally { await store.FailAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow, ValidationRunStatus.Failed, "TEST_COMPLETE", "Retained test evidence."); }
        }
    }

    [RequiresPostgresFact]
    public async Task LegacyFindingJsonWithoutOptionalNullFieldsRemainsReadable()
    {
        await using var db = fixture.CreateDbContext();
        var (store, run, lease) = await RunningAsync(db);
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
            PackageValidationRunTests.Report(run.Binding)));
        var row = await db.Set<PackageValidationFindingRecord>().SingleAsync(item => item.RunId == run.Id && item.Ordinal == 0);
        var json = JsonNode.Parse(row.Json)!.AsObject();
        json.Remove("recommendedAction");
        json["location"]!.AsObject().Remove("files");
        row.Json = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await db.SaveChangesAsync();
        await using var read = fixture.CreateDbContext();

        var page = await new EfCorePackageValidationStore(read).GetFindingsAsync(run.Id, new(Limit: 1));

        Assert.Equal("MISSING_A", Assert.Single(page.Items).Code);
        Assert.Equal(2, page.TotalCount);
    }

    [RequiresPostgresFact]
    public async Task ValidPagesAndProjectionRemainReadableInANewContext()
    {
        await using var db = fixture.CreateDbContext();
        var (store, run, lease) = await RunningAsync(db);
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
            PackageValidationRunTests.Report(run.Binding), Projection(run)));
        await using var read = fixture.CreateDbContext(); var reader = new EfCorePackageValidationStore(read);
        var page = await reader.GetFindingsAsync(run.Id, new(Offset: 1, Limit: 1, Severity: ValidationSeverity.Error));
        Assert.Equal(2, page.TotalCount); Assert.Equal("MISSING_B", Assert.Single(page.Items).Code);
        Assert.Empty((await reader.GetFindingsAsync(run.Id, new(RuleId: "unknown"))).Items);
        using var projection = JsonDocument.Parse((await reader.GetProjectionJsonAsync(run.Id))!);
        Assert.Equal(run.ApplicationId, projection.RootElement.GetProperty("applicationId").GetGuid());
    }

    private static DossierProjection Projection(PackageValidationRun run) => DossierProjection.Create(run.ApplicationId,
        run.SequenceNumber, run.Binding.ProfileSnapshotId, run.Binding.InputManifestDigest, run.Binding.HistoryBaselineDigest,
        run.Binding.ResourceLimitsDigest, "test-v1", true, [], new HistoryBaselineManifest(run.ApplicationId, run.SequenceNumber, []), [], [], [], [run.SequenceNumber]);

    private static async Task<(EfCorePackageValidationStore Store, PackageValidationRun Run, PackageValidationLease Lease)> RunningAsync(RAToolsDbContext db)
    {
        var store = new EfCorePackageValidationStore(db);
        var run = PackageValidationRunTests.NewRun();
        await store.EnqueueAsync(run);
        var lease = (await store.ClaimAsync("r13", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!;
        Assert.Equal(run.Id, lease.Run.Id);
        return (store, run, lease);
    }
}
