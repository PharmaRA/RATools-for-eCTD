using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RATools.Api.Controllers;
using RATools.Api.Contracts;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;

namespace RATools.Tests.Publishing;

public sealed class PublishJobsControllerContractTests
{
    [Fact]
    public void LegacyCreate_ReturnsGoneWithoutInvokingThePublishService()
    {
        var service = new StubPublishJobService();
        var controller = new PublishJobsController(service);

        var result = controller.Create();

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status410Gone, response.StatusCode);
        Assert.Null(service.EnqueuedRequest);
    }

    [Fact]
    public async Task Execute_EnqueuesTheOnlyCreationCommandAndReturnsAccepted()
    {
        var service = new StubPublishJobService();
        var controller = new PublishJobsController(service);
        var applicationId = Guid.NewGuid();

        var result = await controller.Execute(
            new CreatePublishJobRequestBody
            {
                ApplicationId = applicationId,
                SequenceNumber = "0001"
            },
            CancellationToken.None,
            "controller-idempotency-0001");

        var response = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, response.StatusCode);
        Assert.Equal(nameof(PublishJobsController.GetById), response.ActionName);
        Assert.Equal(service.Job, response.Value);
        Assert.Equal(
            new CreatePublishJobRequest(applicationId, "0001", "controller-idempotency-0001"),
            service.EnqueuedRequest);
    }

    private sealed class StubPublishJobService : IPublishJobService
    {
        public PublishJobDto Job { get; } = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0001",
            "Pending",
            null,
            null,
            DateTime.UtcNow,
            null,
            null);

        public CreatePublishJobRequest? EnqueuedRequest { get; private set; }

        public Task<PublishExecutionReportDto> ExecuteAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PublishJobDto> EnqueueExecutionAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
        {
            EnqueuedRequest = request;
            return Task.FromResult(Job);
        }

        public Task<PublishExecutionReportDto> ExecuteQueuedAsync(Guid jobId, CreatePublishJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PublishExecutionReportDto?> GetExecutionReportAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PublishExecutionReportDto?>(null);

        public Task<PublishArtifactsDto?> GetArtifactsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PublishArtifactsDto?>(null);

        public Task<PublishArtifactDownloadDto?> GetArtifactDownloadAsync(Guid id, string artifactName, CancellationToken cancellationToken = default)
            => Task.FromResult<PublishArtifactDownloadDto?>(null);

        public Task<PublishJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PublishJobDto?>(Job);

        public Task<IReadOnlyCollection<PublishJobDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<PublishJobDto>>([Job]);
    }
}
