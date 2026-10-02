using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.Workspaces;
using RATools.Domain.Ctd;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresWorkspaceMutationTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task DocumentMutationAndNodeWriterShareTheApplicationLockAndRevision()
    {
        var id = await SeedAsync();
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var coordinator = Coordinator(first);
        var graph = new CtdNodeGraph(id, IchSectionDefinitions.Current);
        var root = graph.Create("m3-quality", null, new Dictionary<string, string>());
        var nodes = new[] { new SequenceNode(graph, "0000", root.Id, root.IdentityAttributes) };
        Task<long> competing;
        await using (var mutation = await coordinator.AcquireAsync(id, "0000", 0))
        {
            competing = new EfCoreCtdNodeRepository(second).SaveSequenceAsync(graph, "0000", nodes, 0);
            await mutation.CommitAsync(ct => first.Sequences.Where(row => row.ApplicationId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "Winner"), ct));
            Assert.Equal(1, mutation.Revision);
        }
        var conflict = await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => competing);
        Assert.Equal(1, conflict.CurrentRevision);
        await using var verify = fixture.CreateDbContext();
        Assert.Empty(await verify.SequenceNodes.Where(row => row.ApplicationId == id).ToArrayAsync());
        var sequence = await verify.Sequences.SingleAsync(row => row.ApplicationId == id);
        Assert.Equal("Winner", sequence.Description);
        Assert.Equal(1, sequence.WorkspaceRevision);
    }

    [RequiresPostgresFact]
    public async Task CancellationAfterDatabaseWriteRollsBackAndReleasesLockForRetry()
    {
        var id = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        using var cancellation = new CancellationTokenSource();
        await using (var mutation = await Coordinator(context).AcquireAsync(id, "0000", 0))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutation.CommitAsync(async ct =>
            {
                await context.Sequences.Where(row => row.ApplicationId == id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "Cancelled"), ct);
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }, cancellation.Token));
        }
        await using var retryContext = fixture.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var retry = await Coordinator(retryContext).AcquireAsync(id, "0000", 0, timeout.Token);
        var sequence = await retryContext.Sequences.AsNoTracking().SingleAsync(row => row.ApplicationId == id, timeout.Token);
        Assert.Equal("Original", sequence.Description);
        Assert.Equal(0, sequence.WorkspaceRevision);
        await retry.CommitAsync(_ => Task.CompletedTask, timeout.Token);
        Assert.Equal(1, retry.Revision);
    }

    [RequiresPostgresFact]
    public async Task FailedCompensationRollsBackRevisionAndRetainedStateAdvancesOnlyOnce()
    {
        var id = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        await using var mutation = await Coordinator(context).AcquireAsync(id, "0000", 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.ReconcileAsync(true,
            _ => throw new InvalidOperationException("Compensation failed"), CancellationToken.None));
        Assert.Equal(0, await new EfCoreWorkspaceRevisionStore(context).GetRevisionAsync(id, "0000"));
        await mutation.ReconcileAsync(true, _ => Task.CompletedTask, CancellationToken.None);
        await mutation.ReconcileAsync(true, _ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(1, await new EfCoreWorkspaceRevisionStore(context).GetRevisionAsync(id, "0000"));
    }

    private static WorkspaceMutationCoordinator Coordinator(RAToolsDbContext context) =>
        new(new EfCoreWorkspaceRevisionStore(context), new EfCorePersistenceTransaction(context));

    private async Task<Guid> SeedAsync()
    {
        var id = Guid.NewGuid();
        await using var context = fixture.CreateDbContext();
        context.Applications.Add(new ApplicationRecord { Id = id, ApplicationNumber = $"revision-{id:N}", Region = "US",
            SponsorName = "Synthetic", EctdTemplateKey = "us-fda-ectd-3.2.2", WorkingDirectoryPath = $"C:/synthetic/{id:N}", CreatedUtc = DateTime.UtcNow });
        context.Sequences.Add(new SequenceRecord { ApplicationId = id, SequenceNumber = "0000", SubmissionType = "original",
            Description = "Original", CreatedUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
        return id;
    }
}
