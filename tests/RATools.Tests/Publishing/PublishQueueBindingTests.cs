using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Publishing;

public sealed class PublishQueueBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedJobRetainsItsExplicitValidationReferenceAfterReload(bool relationalMapping)
    {
        var validation = new InMemoryPackageValidationStore();
        var applicationId = Guid.NewGuid();
        var run = await CompleteAsync(validation, applicationId);
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase("publish-binding-" + Guid.NewGuid().ToString("N")).Options;
        await using var writer = new RAToolsDbContext(options);
        IPublishJobRepository repository = relationalMapping ? new EfCorePublishJobRepository(writer) : new InMemoryPublishJobRepository();
        var queued = await Service(repository, validation).EnqueueExecutionAsync(new(applicationId, "0001", "same-key", run));
        await using var reader = new RAToolsDbContext(options);
        var loaded = await (relationalMapping ? new EfCorePublishJobRepository(reader) : repository).GetAsync(queued.Id);
        Assert.NotNull(loaded);
        var workerRepository = new InMemoryPublishJobRepository();
        await workerRepository.AddAsync(loaded);
        var lease = await workerRepository.TryClaimNextAsync("new-worker", DateTime.UtcNow.AddSeconds(1), TimeSpan.FromMinutes(1), 3);
        Assert.NotNull(lease);
        var report = await Service(workerRepository, validation).ExecuteClaimedAsync(lease);
        Assert.Equal(run, report.PackageValidationRunId);
        Assert.Equal("Failed", report.PublishJob.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameKeyCannotChangeOrRemoveTheExplicitValidationReference(bool remove)
    {
        var validation = new InMemoryPackageValidationStore();
        var repository = new InMemoryPublishJobRepository();
        var service = Service(repository, validation);
        var applicationId = Guid.NewGuid();
        var firstRun = await CompleteAsync(validation, applicationId);
        var secondRun = await CompleteAsync(validation, applicationId);
        var first = await service.EnqueueExecutionAsync(new(applicationId, "0001", "same-key", firstRun));
        var replay = await service.EnqueueExecutionAsync(new(applicationId, "0001", "same-key", firstRun));
        Assert.Equal(first.Id, replay.Id);
        await Assert.ThrowsAsync<PublishJobIdempotencyConflictException>(() => service.EnqueueExecutionAsync(
            new(applicationId, "0001", "same-key", remove ? null : secondRun)));
        Assert.Single(await repository.ListAsync());
    }

    private static PublishJobService Service(IPublishJobRepository repository, InMemoryPackageValidationStore validation) =>
        // No output is produced after a failed sequence validation. Optional audit failure
        // is handled by the service; artifact collaborators are never asked to open a file.
        new(repository, null!, new FailingValidation(), null!, null!, new PublishArtifactResolver(null!),
            new PublishReportStore(null!), new PublishOutputVerifier(), new SilentQueue(),
            NullLogger<PublishJobService>.Instance, validation);

    private static async Task<Guid> CompleteAsync(InMemoryPackageValidationStore store, Guid applicationId)
    {
        var now = DateTime.UtcNow;
        var binding = new ValidationBinding(new string('a', 64), new string('b', 64), PackageValidationCatalog.IchProfile,
            PackageValidationCatalog.Current.Digest, "test", PackageValidationMode.Development, new PackageReadLimits().Digest());
        var run = new PackageValidationRun(applicationId, "0001", binding, "{}", Guid.NewGuid().ToString("N"), now);
        await store.EnqueueAsync(run);
        var lease = (await store.ClaimAsync("test", now.AddSeconds(1), TimeSpan.FromMinutes(1)))!;
        Assert.True(await store.CompleteAsync(run.Id, lease.Token, lease.Owner, now.AddSeconds(2),
            ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed,
                [new RuleCheckResult("FILE-HREF", CheckStatus.Pass, "Complete.")])));
        return run.Id;
    }

    private sealed class FailingValidation : ISequenceValidationService
    {
        public Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationReportDto(request.ApplicationId, request.SequenceNumber, "test", false,
                [new ValidationIssueDto("Error", "TEST_STOP", "Stop before generating files.")], [], []));
    }

    private sealed class SilentQueue : IPublishJobQueue
    {
        public ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask WaitForWorkAsync(TimeSpan maximumDelay, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
