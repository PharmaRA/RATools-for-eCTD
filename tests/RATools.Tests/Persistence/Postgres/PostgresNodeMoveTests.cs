using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Workspaces;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresNodeMoveTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task MoveCommitsFileBindingAndRevisionAndRejectsACompetingRequest()
    {
        await using var database = fixture.CreateDbContext();
        using var scope = new NodeMoveTestWorkspace(database);
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        var result = await scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1));
        await using var verify = fixture.CreateDbContext();
        var placement = await verify.DocumentPlacements.AsNoTracking().SingleAsync(row => row.Id == item.Placement.Id);
        var document = await verify.Documents.AsNoTracking().SingleAsync(row => row.Id == item.Document.Id);
        var sequence = await verify.Sequences.AsNoTracking().SingleAsync(row => row.ApplicationId == scope.Application.Id);
        Assert.Equal(scope.Beta.NodeInstanceId, placement.NodeInstanceId);
        Assert.Equal(item.Placement.LeafId, placement.LeafId);
        Assert.Equal(7, placement.SortOrder);
        Assert.Equal(result.TargetPath, document.StoragePath);
        Assert.True(File.Exists(document.StoragePath));
        Assert.False(File.Exists(item.Document.StoragePath));
        Assert.Equal(2, sequence.WorkspaceRevision);
        var competitor = new CtdNodePlacementService(new EfCoreCtdNodeRepository(verify), new EfCoreDocumentPlacementRepository(verify),
            new EfCoreDocumentRepository(verify), new EfCoreApplicationRepository(verify), scope.Boundary, scope.Paths, scope.Files,
            new(new EfCoreWorkspaceRevisionStore(verify), new EfCorePersistenceTransaction(verify)), scope.Journal);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => competitor.MoveAsync(item.Placement.Id, new(scope.Alpha.NodeInstanceId, 0, 1)));
    }

    [RequiresPostgresFact]
    public async Task FailedDatabaseWriteRestoresFileAndOriginalRecords()
    {
        await using var database = fixture.CreateDbContext();
        using var scope = new NodeMoveTestWorkspace(database);
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        scope.Placements.FailuresRemaining = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(item.Document.StoragePath, (await new EfCoreDocumentRepository(verify).GetAsync(item.Document.Id))!.StoragePath);
        Assert.Equal(scope.Alpha.NodeInstanceId, (await new EfCoreDocumentPlacementRepository(verify).GetAsync(item.Placement.Id))!.NodeInstanceId);
        Assert.Equal(1, await new EfCoreWorkspaceRevisionStore(verify).GetRevisionAsync(scope.Application.Id, "0000"));
        Assert.True(File.Exists(item.Document.StoragePath));
    }

    [RequiresPostgresFact]
    public async Task FailedRestorePreservesMovedNodeAndAdvancesRevision()
    {
        await using var database = fixture.CreateDbContext();
        using var scope = new NodeMoveTestWorkspace(database);
        await scope.SeedAsync();
        var item = await scope.AddAsync(node: scope.Alpha);
        scope.Placements.FailuresRemaining = 1;
        scope.Files.ThrowBeforeRename = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.MoveAsync(item.Placement.Id, new(scope.Beta.NodeInstanceId, 7, 1)));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(scope.Destination(scope.Beta.NodeInstanceId), (await new EfCoreDocumentRepository(verify).GetAsync(item.Document.Id))!.StoragePath);
        Assert.Equal(scope.Beta.NodeInstanceId, (await new EfCoreDocumentPlacementRepository(verify).GetAsync(item.Placement.Id))!.NodeInstanceId);
        Assert.Equal(2, await new EfCoreWorkspaceRevisionStore(verify).GetRevisionAsync(scope.Application.Id, "0000"));
        Assert.True(File.Exists(scope.Destination(scope.Beta.NodeInstanceId)));
    }
}
