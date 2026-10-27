using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RATools.Api.Controllers;
using RATools.Application.Finalization;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

public sealed class FinalizationsControllerTests
{
    [Fact]
    public async Task MissingParentsReturnTheDocumentedProblemDetails()
    {
        var controller = new FinalizationsController(null!, new InMemoryFinalizationStore());
        var missing = Guid.NewGuid();
        AssertNotFound(await controller.Deliveries(missing, CancellationToken.None), missing);
        AssertNotFound(await controller.Receipts(missing, CancellationToken.None), missing);
    }

    [Fact]
    public async Task ExistingParentsWithoutChildrenReturnEmptyCollections()
    {
        var store = new InMemoryFinalizationStore();
        var controller = new FinalizationsController(null!, store);
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

    private static void AssertNotFound(IActionResult result, Guid missing)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Contains(missing.ToString(), problem.Detail, StringComparison.Ordinal);
    }
}
