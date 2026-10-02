using RATools.Application.Abstractions.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Workspaces;

public sealed class WorkspaceMutationCoordinatorTests
{
    [Fact]
    public async Task ConcurrentMutationsWithSameRevisionAllowOnlyOneCommit()
    {
        var application = new SubmissionApplication("APP-REV", "US", "Sponsor", Path.GetTempPath(), "us-fda-ectd-3.2.2");
        application.CreateSequence("0000", "original-application", "Initial");
        var applications = new InMemoryApplicationRepository();
        await applications.AddAsync(application);
        var coordinator = new WorkspaceMutationCoordinator(
            new InMemoryWorkspaceRevisionStore(applications),
            new InMemoryPersistenceTransaction());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await using var mutation = await coordinator.AcquireAsync(application.Id, "0000", 0);
            await mutation.CommitAsync(async _ => { entered.SetResult(); await release.Task; });
            return mutation.Revision;
        });
        await entered.Task;
        var second = coordinator.AcquireAsync(application.Id, "0000", 0);
        release.SetResult();

        Assert.Equal(1, await first);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(async () => { await using var rejected = await second; });
        Assert.Equal(1, await new InMemoryWorkspaceRevisionStore(applications).GetRevisionAsync(application.Id, "0000"));
    }

    [Fact]
    public async Task FailedMutationDoesNotPublishRevisionOrDomainChanges()
    {
        var application = new SubmissionApplication("APP-ROLLBACK", "US", "Sponsor", Path.GetTempPath(), "us-fda-ectd-3.2.2");
        application.CreateSequence("0000", "original-application", "Initial");
        var applications = new InMemoryApplicationRepository();
        await applications.AddAsync(application);
        var coordinator = new WorkspaceMutationCoordinator(
            new InMemoryWorkspaceRevisionStore(applications),
            new InMemoryPersistenceTransaction());

        await using var mutation = await coordinator.AcquireAsync(application.Id, "0000", 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.CommitAsync(async ct =>
        {
            var draft = (await applications.GetAsync(application.Id, ct))!;
            draft.CreateSequence("0001", "amendment", "Must roll back");
            await applications.UpdateAsync(draft, ct);
            throw new InvalidOperationException("simulated failure");
        }));

        var reloaded = await applications.GetAsync(application.Id);
        Assert.Equal(0, Assert.Single(reloaded!.Sequences).WorkspaceRevision);
    }
}
