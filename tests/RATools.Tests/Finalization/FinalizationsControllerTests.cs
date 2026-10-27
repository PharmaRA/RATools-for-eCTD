using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RATools.Api.Controllers;
using RATools.Application.Finalization;
using RATools.Application.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

public sealed class FinalizationsControllerTests
{
    [Fact]
    public async Task MissingParentsReturnTheDocumentedProblemDetails()
    {
        var store = new InMemoryFinalizationStore();
        var controller = new FinalizationsController(null!, store, Drafts(store));
        var missing = Guid.NewGuid();
        AssertNotFound(await controller.Deliveries(missing, CancellationToken.None), missing);
        AssertNotFound(await controller.Receipts(missing, CancellationToken.None), missing);
    }

    [Fact]
    public async Task ExistingParentsWithoutChildrenReturnEmptyCollections()
    {
        var store = new InMemoryFinalizationStore();
        var controller = new FinalizationsController(null!, store, Drafts(store));
        var digest = new string('a', 64);
        var snapshot = FinalizedSequence.Create(Guid.NewGuid(), "0001", 1, digest, "profile", "3.2.2", null,
            digest, Guid.NewGuid(), digest, digest, PackageValidationMode.Formal, true, [],
            FinalizationService.ToolVersion, "empty-children", DateTime.UtcNow);
        await store.RegisterAsync(snapshot);
        var deliveries = Assert.IsType<OkObjectResult>(await controller.Deliveries(snapshot.Id, CancellationToken.None));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DeliveryRecord>>(deliveries.Value));
        var delivery = DeliveryRecord.Create(snapshot.Id, digest, "gateway", null, null, "operator", DateTime.UtcNow);
        await store.AppendDeliveryAsync(delivery);
        var receipts = Assert.IsType<OkObjectResult>(await controller.Receipts(delivery.Id, CancellationToken.None));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DeliveryReceipt>>(receipts.Value));
    }

    // The read-only endpoints under test never open a draft; the service still needs a
    // coordinator, which is constructed with empty in-memory stores.
    private static FinalizationDraftService Drafts(IFinalizationStore store) =>
        new(store, new WorkspaceMutationCoordinator(new NoRevisionStore(), new PassthroughPersistenceTransaction()));

    private sealed class NoRevisionStore : RATools.Application.Abstractions.Persistence.IWorkspaceRevisionStore
    {
        public Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(new Noop());
        public Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(null);
        public Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static void AssertNotFound(IActionResult result, Guid missing)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Contains(missing.ToString(), problem.Detail, StringComparison.Ordinal);
    }
}
