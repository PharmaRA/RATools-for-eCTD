using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Data.Common;
using RATools.Application.Applications;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Domain.Ctd;
using RATools.Tests.Applications;
using static RATools.Tests.Applications.NodeImportWorkspace;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresApplicationImportTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task RealImportPreservesIndependentFixtureAndBackboneSources()
    {
        await using var database = fixture.CreateDbContext();
        using var scope = new NodeImportWorkspace(database);
        scope.CopyPublisherFixture();
        var result = await scope.ImportAsync();
        Assert.Equal(4, result.ImportedSequenceCount);
        Assert.Equal(14, result.ImportedPlacementCount);
        database.ChangeTracker.Clear();
        await ApplicationImportNodeTests.AssertFixtureAsync(scope, result.ApplicationId);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(8, await verify.ImportedBackbones.CountAsync(row => row.ApplicationId == result.ApplicationId));
        Assert.All(await verify.Sequences.Where(row => row.ApplicationId == result.ApplicationId).ToArrayAsync(),
            row => Assert.Equal(1, row.WorkspaceRevision));
        var downgrade = await Assert.ThrowsAsync<PostgresException>(() => verify.GetService<IMigrator>()
            .MigrateAsync("20260908131138_AddCtdNodeInstances"));
        Assert.Equal("55000", downgrade.SqlState);
        Assert.Equal(8, await verify.ImportedBackbones.CountAsync(row => row.ApplicationId == result.ApplicationId));
        Assert.Empty(await verify.Database.GetPendingMigrationsAsync());
    }

    [RequiresPostgresFact]
    public async Task AmbiguityDiscoveredInLaterSequencePersistsAllSeparateInstances()
    {
        await using var database = fixture.CreateDbContext();
        using var scope = new NodeImportWorkspace(database);
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("first"))));
        await scope.WriteAsync("0001", Quality(Substance("Alpha", Leaf("second", "second.pdf")), Substance("Alpha", Leaf("third", "third.pdf"))));
        var result = await scope.ImportAsync();
        database.ChangeTracker.Clear();
        var workspace = (await scope.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        var instances = workspace.Graph.Nodes.Values.Where(node => node.DefinitionKey == "m3-2-s-drug-substance").ToArray();
        Assert.Equal(3, instances.Length);
        Assert.All(instances, node => Assert.Equal(CtdIdentityStatus.Ambiguous, node.IdentityStatus));
        Assert.Equal(NodeMetadataStatus.LegacyUnresolved, workspace.Nodes.Single(node => node.CtdSection == "m3.2.s.4.1").MetadataStatus);
    }

    [RequiresPostgresFact]
    public Task InterruptedImportRollsBackEveryTable() => AssertRollbackAsync(cancel: false);

    [RequiresPostgresFact]
    public Task CancelledImportRollsBackWithAnIndependentToken() => AssertRollbackAsync(cancel: true);

    [RequiresPostgresFact]
    public async Task UnconfirmedRollbackReportsTheAffectedImportAndUsesAnIndependentTimeout()
    {
        await using var connectionSource = fixture.CreateDbContext();
        var fault = new RollbackFault();
        await using var database = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseNpgsql(connectionSource.Database.GetConnectionString()).AddInterceptors(fault).Options);
        using var scope = new NodeImportWorkspace(database);
        using var cancellation = new CancellationTokenSource();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("first"))));
        scope.Placements.CancelAfterAdd = cancellation;
        var failure = await Assert.ThrowsAsync<ApplicationImportCleanupException>(() => scope.ImportAsync(cancellation.Token));
        Assert.True(fault.IndependentTokenObserved);
        Assert.Equal(scope.Batch!.Application.Id, failure.ApplicationId);
        Assert.Equal(scope.Batch.Documents.Select(item => item.Id), failure.UnconfirmedDocumentIds);
        Assert.Equal(scope.Batch.Placements.Select(item => item.Id), failure.UnconfirmedPlacementIds);
        Assert.Contains("IMPORT_ROLLBACK_INCOMPLETE", failure.Message);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    private sealed class RollbackFault : DbTransactionInterceptor
    {
        public bool IndependentTokenObserved { get; private set; }
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            IndependentTokenObserved = cancellationToken.CanBeCanceled && !cancellationToken.IsCancellationRequested;
            throw new IOException("Injected rollback failure");
        }
    }

    private async Task AssertRollbackAsync(bool cancel)
    {
        await using var database = fixture.CreateDbContext();
        using var scope = new NodeImportWorkspace(database);
        using var cancellation = new CancellationTokenSource();
        await scope.WriteAsync("0000", Quality(Substance("Alpha", Leaf("original"))));
        scope.Placements.FailAfterAdd = !cancel;
        scope.Placements.CancelAfterAdd = cancel ? cancellation : null;
        var failure = await Record.ExceptionAsync(() => scope.ImportAsync(cancellation.Token));
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(failure); else Assert.IsType<IOException>(failure);
        Assert.Empty(database.ChangeTracker.Entries());
        var id = scope.Batch!.Application.Id;
        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.Applications.AnyAsync(row => row.Id == id));
        Assert.False(await verify.Sequences.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await verify.CtdNodeInstances.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await verify.SequenceNodes.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await verify.DocumentPlacements.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await verify.ImportedBackbones.AnyAsync(row => row.ApplicationId == id));
        var documentIds = scope.Batch.Documents.Select(item => item.Id).ToArray();
        Assert.False(await verify.Documents.AnyAsync(row => documentIds.Contains(row.Id)));
        Assert.True(File.Exists(Path.Combine(scope.Root, "0000", "specification.pdf")));
    }
}
