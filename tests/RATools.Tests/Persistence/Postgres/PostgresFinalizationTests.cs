using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresFinalizationTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [RequiresPostgresFact]
    public async Task SnapshotIdempotencyAndAppendOnlyDeliveryFactsSurviveInstances()
    {
        var applicationId = Guid.NewGuid();
        var sequenceNumber = UniqueSequence();
        var run = await SeedCompletedRunAsync(applicationId, sequenceNumber);
        var snapshot = Snapshot(applicationId, sequenceNumber, run.Id, run.Binding);
        await using (var first = fixture.CreateDbContext())
            Assert.True((await new EfCoreFinalizationStore(first).RegisterAsync(snapshot)).Created);
        await using (var replay = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(replay);
            var result = await store.RegisterAsync(Snapshot(applicationId, sequenceNumber, run.Id, run.Binding,
                key: snapshot.IdempotencyKey));
            Assert.False(result.Created);
            Assert.Equal(snapshot.Id, result.Sequence.Id);
            // The same key with a different identity digest cannot silently reuse content.
            await Assert.ThrowsAsync<FinalizationIdempotencyConflictException>(() =>
                store.RegisterAsync(Snapshot(applicationId, sequenceNumber, run.Id, run.Binding,
                    key: snapshot.IdempotencyKey, manifestSeed: "other-manifest")));
            // The same content under a new key is still one snapshot.
            await Assert.ThrowsAsync<FinalizationSnapshotExistsException>(() =>
                store.RegisterAsync(Snapshot(applicationId, sequenceNumber, run.Id, run.Binding, key: "second-key")));
        }
        await using (var append = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(append);
            await store.AppendDeliveryAsync(DeliveryRecord.Create(snapshot.Id, Digest("package"),
                "gateway", "EXT-1", null, "operator", Now));
            await store.AppendReceiptAsync(DeliveryReceipt.Create((await store.ListDeliveriesAsync(snapshot.Id))[0].Id,
                DeliveryReceiptKind.Transmission, "Accepted", "<ack />", "EXT-1", "operator", Now));
        }
        await using var verify = fixture.CreateDbContext();
        var reloaded = new EfCoreFinalizationStore(verify);
        var stored = await reloaded.GetAsync(snapshot.Id);
        Assert.NotNull(stored);
        Assert.Equal(snapshot.RequestDigest, stored!.RequestDigest);
        Assert.Equal(snapshot.ValidationRunId, stored.ValidationRunId);
        Assert.Single(await reloaded.ListDeliveriesAsync(snapshot.Id));
        Assert.Single(await reloaded.ListReceiptsAsync((await reloaded.ListDeliveriesAsync(snapshot.Id))[0].Id));
    }

    [RequiresPostgresFact]
    public async Task SubMicrosecondTimestampsRoundTripAndContentMayBeFinalizedPerMode()
    {
        var applicationId = Guid.NewGuid();
        var sequenceNumber = UniqueSequence();
        var run = await SeedCompletedRunAsync(applicationId, sequenceNumber);
        var snapshot = Snapshot(applicationId, sequenceNumber, run.Id, run.Binding);
        var delivery = DeliveryRecord.Create(snapshot.Id, Digest("package"), "gateway", null, null, "operator",
            Now.AddTicks(7));
        await using (var append = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(append);
            await store.RegisterAsync(snapshot);
            await store.AppendDeliveryAsync(delivery);
            await store.AppendReceiptAsync(DeliveryReceipt.Create(delivery.Id, DeliveryReceiptKind.Transmission,
                "Accepted", "<ack />", null, "operator", Now.AddTicks(9)));
        }
        await using (var verify = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(verify);
            // Reloading must reproduce the committed digests after PostgreSQL truncates
            // the timestamp to microseconds.
            var storedDelivery = Assert.Single(await store.ListDeliveriesAsync(snapshot.Id));
            Assert.Equal(delivery.Digest, storedDelivery.Digest);
            var storedReceipt = Assert.Single(await store.ListReceiptsAsync(storedDelivery.Id));
            Assert.Equal(0, storedReceipt.RecordedUtc.Ticks % 10);
            Assert.True(await store.DeliveryExistsAsync(storedDelivery.Id));
            // The same content may also be finalized formally: a development snapshot is
            // not delivery authorization and must not block a formal finalization.
            var formal = Snapshot(applicationId, sequenceNumber, run.Id, run.Binding,
                key: "formal-same-content-" + sequenceNumber, mode: PackageValidationMode.Formal);
            var result = await store.RegisterAsync(formal);
            Assert.True(result.Created);
            Assert.Equal(PackageValidationMode.Formal, result.Sequence.ValidationMode);
        }
    }

    [RequiresPostgresFact]
    public async Task SnapshotOfAnotherSequencesDigestIsRejectedByTheContentConstraint()
    {
        var applicationId = Guid.NewGuid();
        var sequenceNumber = UniqueSequence();
        var run = await SeedCompletedRunAsync(applicationId, sequenceNumber);
        await using var context = fixture.CreateDbContext();
        var store = new EfCoreFinalizationStore(context);
        await store.RegisterAsync(Snapshot(applicationId, sequenceNumber, run.Id, run.Binding));
        // A different sequence may reuse the same validated bytes: content uniqueness is
        // scoped to the sequence, not global.
        var otherSequence = UniqueSequence();
        var otherRun = await SeedCompletedRunAsync(applicationId, otherSequence);
        var other = await store.RegisterAsync(Snapshot(applicationId, otherSequence, otherRun.Id, otherRun.Binding,
            manifestSeed: run.Binding.InputManifestDigest));
        Assert.True(other.Created);
        Assert.Throws<FinalizationSnapshotExistsException>(() => store.RegisterAsync(
            Snapshot(applicationId, sequenceNumber, run.Id, run.Binding, key: "duplicate-content")).GetAwaiter().GetResult());
    }

    [RequiresPostgresFact]
    public async Task DowngradeRefusesToDiscardCommittedSnapshots()
    {
        var applicationId = Guid.NewGuid();
        var sequenceNumber = UniqueSequence();
        var run = await SeedCompletedRunAsync(applicationId, sequenceNumber);
        await using var context = fixture.CreateDbContext();
        var store = new EfCoreFinalizationStore(context);
        await store.RegisterAsync(Snapshot(applicationId, sequenceNumber, run.Id, run.Binding));
        // The Down guard raises before any table is dropped, so a failed downgrade is a
        // no-op on the shared schema. It must refuse once a committed snapshot exists.
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261018042240_AddPackageValidationProjection"));
        Assert.Equal("55000", error.SqlState);
        // EF applies each migration's Down independently: the newer, unguarded draft
        // migration was already reverted before the snapshot guard aborted the downgrade.
        // Restore the shared test database before asserting that it is current again.
        await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotNull(await store.GetAsync((await store.ListBySequenceAsync(applicationId, sequenceNumber))[0].Id));
    }

    [RequiresPostgresFact]
    public async Task DraftContinuationIsUniquePerSnapshotAndRefusesDowngrade()
    {
        var applicationId = Guid.NewGuid();
        var sequenceNumber = UniqueSequence();
        var run = await SeedCompletedRunAsync(applicationId, sequenceNumber);
        var snapshot = Snapshot(applicationId, sequenceNumber, run.Id, run.Binding);
        var draft = FinalizedDraftRevision.Create(snapshot.Id, applicationId, sequenceNumber,
            snapshot.SourceWorkspaceRevision, snapshot.SourceWorkspaceRevision + 1, "draft-" + sequenceNumber, Now);
        await using (var append = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(append);
            await store.RegisterAsync(snapshot);
            Assert.Equal(draft.Id, (await store.AppendDraftAsync(draft)).Id);
        }
        await using (var replay = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(replay);
            // Outside an ambient transaction, both unique keys resolve to the committed
            // row: an exact key replay and a competing continuation of the same snapshot
            // must not create a second row.
            Assert.Equal(draft.Id, (await store.AppendDraftAsync(FinalizedDraftRevision.Create(snapshot.Id,
                applicationId, sequenceNumber, snapshot.SourceWorkspaceRevision, snapshot.SourceWorkspaceRevision + 1,
                "draft-" + sequenceNumber, Now.AddMinutes(1)))).Id);
            Assert.Equal(draft.Id, (await store.AppendDraftAsync(FinalizedDraftRevision.Create(snapshot.Id,
                applicationId, sequenceNumber, snapshot.SourceWorkspaceRevision, snapshot.SourceWorkspaceRevision + 2,
                "other-draft-" + sequenceNumber, Now.AddMinutes(2)))).Id);
            var loaded = await store.FindDraftAsync(snapshot.Id);
            Assert.NotNull(loaded);
            Assert.Equal(draft.Digest, loaded!.Digest);
            Assert.Equal("draft-" + sequenceNumber, loaded.IdempotencyKey);
        }
        await using (var transactional = fixture.CreateDbContext())
        {
            var store = new EfCoreFinalizationStore(transactional);
            // Inside the coordinator's transaction PostgreSQL aborts on the violation;
            // the conflict must be classified, not surfaced as a database 500.
            var duplicateKey = FinalizedDraftRevision.Create(snapshot.Id, applicationId, sequenceNumber,
                snapshot.SourceWorkspaceRevision, snapshot.SourceWorkspaceRevision + 1, "another-key-" + sequenceNumber,
                Now.AddMinutes(3));
            await Assert.ThrowsAsync<FinalizationDraftAlreadyOpenException>(() =>
                new EfCorePersistenceTransaction(transactional).ExecuteAsync(
                    ct => store.AppendDraftAsync(duplicateKey, ct)));
            Assert.Empty((await transactional.Database.GetPendingMigrationsAsync()).ToArray());

            var otherSnapshot = Snapshot(applicationId, UniqueSequence(), run.Id, run.Binding,
                key: "transactional-" + sequenceNumber);
            await store.RegisterAsync(otherSnapshot);
            var reused = FinalizedDraftRevision.Create(otherSnapshot.Id, applicationId, otherSnapshot.SequenceNumber,
                otherSnapshot.SourceWorkspaceRevision, otherSnapshot.SourceWorkspaceRevision + 1,
                "draft-" + sequenceNumber, Now.AddMinutes(4));
            await Assert.ThrowsAsync<FinalizationIdempotencyConflictException>(() =>
                new EfCorePersistenceTransaction(transactional).ExecuteAsync(
                    ct => store.AppendDraftAsync(reused, ct)));
        }
        await using var downgrade = fixture.CreateDbContext();
        try
        {
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => downgrade.GetService<
                Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261018071412_RefineFinalizationUniqueness"));
            Assert.Equal("55000", error.SqlState);
            await downgrade.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync();
            Assert.Empty(await downgrade.Database.GetPendingMigrationsAsync());
            Assert.NotNull(await new EfCoreFinalizationStore(downgrade).FindDraftAsync(snapshot.Id));
        }
        finally
        {
            // Drafts are guarded evidence on downgrade; remove this test's row so later
            // downgrade tests in the shared database exercise their own guard.
            await downgrade.Set<FinalizedDraftRevisionRecord>().Where(row => row.FinalizedSequenceId == snapshot.Id)
                .ExecuteDeleteAsync();
        }
    }

    private async Task<PackageValidationRun> SeedCompletedRunAsync(Guid applicationId, string sequenceNumber)
    {
        var binding = new ValidationBinding(Digest("input-" + sequenceNumber), Digest("history"),
            PackageValidationCatalog.UsProfile, PackageValidationCatalog.Current.Digest, "package-delivery-v3",
            PackageValidationMode.Development, new PackageReadLimits().Digest());
        var run = new PackageValidationRun(applicationId, sequenceNumber, binding,
            $"{{\"inputId\":\"{sequenceNumber}\"}}", Guid.NewGuid().ToString("N"), Now);
        await using var context = fixture.CreateDbContext();
        var store = new EfCorePackageValidationStore(context);
        await store.EnqueueAsync(run);
        var lease = (await store.ClaimAsync("finalizer-" + sequenceNumber, Now, TimeSpan.FromMinutes(1)))!;
        var report = ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed,
            [new RuleCheckResult("FILE-HREF", CheckStatus.Pass, "References exist.")]);
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, Now, report));
        return (await store.GetRunAsync(run.Id))!;
    }

    private static FinalizedSequence Snapshot(Guid applicationId, string sequenceNumber, Guid runId,
        ValidationBinding binding, string? key = null, string? manifestSeed = null,
        PackageValidationMode mode = PackageValidationMode.Development) =>
        FinalizedSequence.Create(applicationId, sequenceNumber, 1, Digest(manifestSeed ?? binding.InputManifestDigest),
            PackageValidationCatalog.UsProfile, "3.2.2", "3.3", binding.HistoryBaselineDigest, runId,
            binding.InputManifestDigest, Digest("report"), mode, mode == PackageValidationMode.Formal, [],
            FinalizationService.ToolVersion, key ?? Guid.NewGuid().ToString("N"), Now);

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    private static string UniqueSequence()
    {
        // Sequence numbers are exactly four digits; derive a per-test value that cannot
        // collide with another test's application-scoped content.
        var value = Math.Abs(Guid.NewGuid().GetHashCode()) % 9000 + 1000;
        return value.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
    }
}
