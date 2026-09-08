using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresCtdNodeTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task NodesPersistAttributesAcrossSequencesAndRejectConcurrentRevisions()
    {
        var app = await SeedAsync();
        var graph = new CtdNodeGraph(app, IchSectionDefinitions.Current);
        var root = graph.Create("m3-quality", null, new Dictionary<string, string>());
        var body = graph.Create("m3-2-body-of-data", root.Id, new Dictionary<string, string>());
        var attributes = new Dictionary<string, string> { ["substance"] = "Drug A", ["manufacturer"] = "Alpha" };
        var substance = graph.Create("m3-2-s-drug-substance", body.Id, attributes);
        var nodes = graph.Nodes.Values.Select(node => new SequenceNode(graph, "0000", node.Id, node.IdentityAttributes,
            node.Id == substance.Id ? "Original Alpha" : null, 0, node.Id == substance.Id ? "drug-a-alpha" : null)).ToArray();
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var results = await Task.WhenAll(TrySave(first), TrySave(second));
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        first.ChangeTracker.Clear();
        var loaded = await new EfCoreCtdNodeRepository(first).GetSequenceAsync(app, "0000");
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.WorkspaceRevision);
        var alpha = Assert.Single(loaded.Nodes, node => node.NodeInstanceId == substance.Id);
        Assert.Equal("Alpha", alpha.Attributes["manufacturer"]);
        Assert.Equal("drug-a-alpha", alpha.StorageSegment);
        Assert.Equal(substance.IdentityKey, loaded.Graph.Get(substance.Id).IdentityKey);

        first.Sequences.Add(new SequenceRecord { ApplicationId = app, SequenceNumber = "0001", SubmissionType = "amendment", Description = "later", CreatedUtc = DateTime.UtcNow });
        await first.SaveChangesAsync();
        var later = nodes.Select(node => new SequenceNode(graph, "0001", node.NodeInstanceId, node.Attributes, "Later title", 20, node.StorageSegment)).ToArray();
        Assert.Equal(1, await new EfCoreCtdNodeRepository(first).SaveSequenceAsync(graph, "0001", later, 0));
        var history = await new EfCoreCtdNodeRepository(first).GetSequenceAsync(app, "0000");
        Assert.Equal("Original Alpha", Assert.Single(history!.Nodes, node => node.NodeInstanceId == substance.Id).Title);
        Assert.Equal(3, await first.CtdNodeInstances.CountAsync(row => row.ApplicationId == app));

        async Task<bool> TrySave(RAToolsDbContext context)
        {
            try { await new EfCoreCtdNodeRepository(context).SaveSequenceAsync(graph, "0000", nodes, 0); return true; }
            catch (WorkspaceRevisionConflictException error) { Assert.Equal(1, error.CurrentRevision); return false; }
        }
    }

    [RequiresPostgresFact]
    public async Task BackfillPreviewAndRepeatPreserveLeafIdsAndDoNotGuessIdentity()
    {
        var app = await SeedAsync();
        var firstId = await SeedPlacementAsync(app, "m3.2.s.4.1", "original-alpha-leaf");
        var secondId = await SeedPlacementAsync(app, "m3.2.s.4.1", "original-beta-leaf");
        var simpleId = await SeedPlacementAsync(app, "m2.5", "old-overview");
        await using var context = fixture.CreateDbContext();
        var applicationRepository = new EfCoreApplicationRepository(context);
        var legacyApplication = await applicationRepository.GetAsync(app);
        Assert.Equal(0, Assert.Single(legacyApplication!.Sequences).WorkspaceRevision);
        var backfill = new EfCoreNodeBackfill(context);
        var preview = await backfill.RunAsync(app, "0000", true);
        Assert.Equal(3, preview.BoundPlacements);
        Assert.Equal(2, preview.Diagnostics.Count);
        Assert.Equal(0, await context.CtdNodeInstances.CountAsync(row => row.ApplicationId == app));
        Assert.Equal(0, await context.NodeBackfillCheckpoints.CountAsync(row => row.ApplicationId == app));
        context.ChangeTracker.Clear();
        var applied = await backfill.RunAsync(app, "0000", false);
        Assert.Equal(1, applied.WorkspaceRevision);
        await applicationRepository.UpdateAsync(legacyApplication);
        Assert.Equal(1, Assert.Single((await applicationRepository.GetAsync(app))!.Sequences).WorkspaceRevision);
        var originals = await context.DocumentPlacements.AsNoTracking().Where(row => row.ApplicationId == app).ToDictionaryAsync(row => row.Id);
        var ids = await context.CtdNodeInstances.AsNoTracking().Where(row => row.ApplicationId == app).Select(row => row.Id).ToArrayAsync();
        Assert.NotEqual(originals[firstId].NodeInstanceId, originals[secondId].NodeInstanceId);
        Assert.Equal("original-alpha-leaf", originals[firstId].LeafId);
        Assert.Equal("original-beta-leaf", originals[secondId].LeafId);
        Assert.Equal("old-overview", originals[simpleId].LeafId);
        Assert.All(await context.CtdNodeInstances.Where(row => row.ApplicationId == app && row.DefinitionKey == "m3-2-s-drug-substance").ToArrayAsync(),
            row => Assert.Equal("{}", row.IdentityAttributesJson));
        context.ChangeTracker.Clear();
        var repeated = await backfill.RunAsync(app, "0000", false);
        Assert.False(repeated.Changed);
        Assert.Equal(1, repeated.WorkspaceRevision);
        Assert.Equal(ids.Order(), (await context.CtdNodeInstances.Where(row => row.ApplicationId == app).Select(row => row.Id).ToArrayAsync()).Order());
        Assert.Equal(1, await context.NodeBackfillCheckpoints.CountAsync(row => row.ApplicationId == app));
        var placementRepository = new EfCoreDocumentPlacementRepository(context);
        var restoredPlacement = await placementRepository.GetAsync(firstId);
        var loadedWorkspace = await new EfCoreCtdNodeRepository(context).GetSequenceAsync(app, "0000");
        Assert.NotNull(restoredPlacement);
        restoredPlacement.BindToNode(loadedWorkspace!.Nodes.Single(node => node.NodeInstanceId == restoredPlacement.NodeInstanceId), 17);
        restoredPlacement.ReviseTitle("Updated draft title");
        Assert.True(await placementRepository.UpdateAsync(restoredPlacement));
        var reread = await placementRepository.GetAsync(firstId);
        Assert.Equal(originals[firstId].NodeInstanceId, reread!.NodeInstanceId);
        Assert.Equal("original-alpha-leaf", reread.LeafId);
        Assert.Equal(17, reread.SortOrder);
    }

    [RequiresPostgresFact]
    public async Task CancellationAfterDatabaseWritesRollsBackNodesBindingsRevisionAndCheckpoint()
    {
        var app = await SeedAsync();
        var placement = await SeedPlacementAsync(app, "m2.5", "cancel-test-leaf");
        using var cancelled = new CancellationTokenSource();
        var options = new DbContextOptionsBuilder<RAToolsDbContext>().UseNpgsql(PostgresTestEnvironment.ConnectionString)
            .AddInterceptors(new CancelAfterSave(cancelled)).Options;
        await using (var interrupted = new RAToolsDbContext(options))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EfCoreNodeBackfill(interrupted).RunAsync(app, "0000", false, cancelled.Token));
        await using var read = fixture.CreateDbContext();
        Assert.Equal(0, await read.CtdNodeInstances.CountAsync(row => row.ApplicationId == app));
        Assert.Equal(0, await read.SequenceNodes.CountAsync(row => row.ApplicationId == app));
        Assert.Equal(0, await read.NodeBackfillCheckpoints.CountAsync(row => row.ApplicationId == app));
        Assert.Null((await read.DocumentPlacements.SingleAsync(row => row.Id == placement)).NodeInstanceId);
        Assert.Equal(0, (await read.Sequences.SingleAsync(row => row.ApplicationId == app)).WorkspaceRevision);
        Assert.Equal(1, (await new EfCoreNodeBackfill(read).RunAsync(app, "0000", false)).BoundPlacements);
    }

    [RequiresPostgresFact]
    public async Task DatabaseEnforcesSingletonRootsShapeAndImmutableParents()
    {
        var app = await SeedAsync();
        await SeedPlacementAsync(app, "m2.5", "root-guard");
        await using var context = fixture.CreateDbContext();
        await new EfCoreNodeBackfill(context).RunAsync(app, "0000", false);
        var root = await context.CtdNodeInstances.AsNoTracking().SingleAsync(row => row.ApplicationId == app && row.ParentInstanceId == null);
        await RejectSql("23505", "INSERT INTO ctd_node_instances SELECT {0}, \"ApplicationId\", \"ParentInstanceId\", \"DefinitionVersion\", \"DefinitionKey\", \"Repeatable\", \"Kind\", {1}, \"IdentityComparisonVersion\", \"IdentityStatus\", \"IdentityAttributesJson\" FROM ctd_node_instances WHERE \"Id\" = {2}", Guid.NewGuid(), new string('a', 64), root.Id);
        await RejectSql("23514", "UPDATE ctd_node_instances SET \"ParentInstanceId\" = \"Id\" WHERE \"Id\" = {0}", root.Id);
        await RejectSql("23514", "UPDATE ctd_definitions SET \"Repeatable\" = TRUE WHERE \"Version\" = {0} AND \"DefinitionKey\" = {1}", root.DefinitionVersion, root.DefinitionKey);
        await RejectSql("23503", "INSERT INTO ctd_node_instances SELECT {0}, \"ApplicationId\", \"ParentInstanceId\", \"DefinitionVersion\", \"DefinitionKey\", TRUE, \"Kind\", {1}, \"IdentityComparisonVersion\", \"IdentityStatus\", \"IdentityAttributesJson\" FROM ctd_node_instances WHERE \"Id\" = {2}", Guid.NewGuid(), new string('b', 64), root.Id);
        await RejectSql("23514", "UPDATE sequence_nodes SET \"AttributesJson\" = {0}::jsonb WHERE \"ApplicationId\" = {1} AND \"NodeInstanceId\" = {2}", "{\"arbitrary\":\"value\"}", app, root.Id);
        await RejectSql("23514", "UPDATE sequences SET \"WorkspaceRevision\" = -1 WHERE \"ApplicationId\" = {0}", app);
        await RejectSql("23514", "UPDATE sequences SET \"WorkspaceRevision\" = 9007199254740992 WHERE \"ApplicationId\" = {0}", app);

        async Task RejectSql(string state, string sql, params object[] parameters)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, parameters));
            Assert.Equal(state, error.SqlState);
        }
    }

    [RequiresPostgresFact]
    public async Task ForeignKeyRejectsCrossApplicationPlacementAndApplicationDeletionCascadesOnlyItsNodes()
    {
        var app = await SeedAsync();
        var other = await SeedAsync();
        var placementId = await SeedPlacementAsync(app, "m2.5", "own-leaf");
        await SeedPlacementAsync(other, "m2.5", "other-leaf");
        await using var context = fixture.CreateDbContext();
        await new EfCoreNodeBackfill(context).RunAsync(app, "0000", false);
        context.ChangeTracker.Clear();
        await new EfCoreNodeBackfill(context).RunAsync(other, "0000", false);
        var otherId = (await context.DocumentPlacements.AsNoTracking().SingleAsync(row => row.ApplicationId == other)).NodeInstanceId;
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE document_placements SET \"NodeInstanceId\" = {0} WHERE \"Id\" = {1}", otherId!, placementId));
        Assert.Equal("23503", error.SqlState);
        var bound = await context.DocumentPlacements.AsNoTracking().SingleAsync(row => row.Id == placementId);
        error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            "DELETE FROM sequence_nodes WHERE \"ApplicationId\" = {0} AND \"NodeInstanceId\" = {1}", app, bound.NodeInstanceId!));
        Assert.Equal("23503", error.SqlState);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM applications WHERE \"Id\" = {0}", app);
        Assert.Equal(0, await context.CtdNodeInstances.CountAsync(row => row.ApplicationId == app));
        Assert.Equal(0, await context.SequenceNodes.CountAsync(row => row.ApplicationId == app));
        Assert.Equal(0, await context.NodeBackfillCheckpoints.CountAsync(row => row.ApplicationId == app));
        Assert.True(await context.CtdNodeInstances.AnyAsync(row => row.ApplicationId == other));
        Assert.True(await context.Documents.AnyAsync(row => row.Id == bound.DocumentId));
    }

    private async Task<Guid> SeedAsync()
    {
        var id = Guid.NewGuid();
        await using var context = fixture.CreateDbContext();
        context.Applications.Add(new ApplicationRecord { Id = id, ApplicationNumber = $"ctd-{id:N}", Region = "US", SponsorName = "Synthetic",
            EctdTemplateKey = "us-fda-ectd-3.2.2", WorkingDirectoryPath = $"C:/synthetic/{id:N}", CreatedUtc = DateTime.UtcNow });
        context.Sequences.Add(new SequenceRecord { ApplicationId = id, SequenceNumber = "0000", SubmissionType = "original", Description = "synthetic", CreatedUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedPlacementAsync(Guid app, string section, string leafId)
    {
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await using var context = fixture.CreateDbContext();
        context.Documents.Add(new DocumentRecord { Id = documentId, FileName = "same-name.pdf", MediaType = "application/pdf", FileSize = 1,
            Sha256 = "synthetic", Md5 = "synthetic", StoragePath = $"C:/synthetic/{documentId:N}/same-name.pdf", CreatedUtc = DateTime.UtcNow });
        context.DocumentPlacements.Add(new DocumentPlacementRecord { Id = id, ApplicationId = app, SequenceNumber = "0000", DocumentId = documentId,
            CtdSection = section, LeafId = leafId, Operation = "new", Title = "Synthetic", CreatedUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
        return id;
    }

    private sealed class CancelAfterSave(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
