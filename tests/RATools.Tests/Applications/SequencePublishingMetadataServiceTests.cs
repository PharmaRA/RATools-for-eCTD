using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Application.Standards;
using RATools.Domain.Applications;

namespace RATools.Tests.Applications;

public sealed class SequencePublishingMetadataServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsDefaultFdaMetadataFromApplicationAndSequence()
    {
        var applicationId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "IND-001",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial sequence", DateTime.UtcNow)],
            Path.Combine(Path.GetTempPath(), "IND-001"),
            "us-fda-ectd-3.2.2");
        var service = new SequencePublishingMetadataService(
            new StubApplicationRepository(application),
            new FdaEctd322StandardsProfileProvider());

        var metadata = await service.GetAsync(applicationId, "0000");

        Assert.NotNull(metadata);
        Assert.Equal(applicationId, metadata!.ApplicationId);
        Assert.Equal("0000", metadata.SequenceNumber);
        Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", metadata.StandardsProfile);
        Assert.Equal("Demo Sponsor", metadata.ApplicantName);
        Assert.Equal("original-application", metadata.SubmissionType);
        Assert.Equal("Initial sequence", metadata.SequenceDescription);
        Assert.Null(metadata.ApplicationType);
        Assert.Null(metadata.SubmissionSubtype);
        Assert.Null(metadata.FormType);
    }

    [Fact]
    public async Task UpdateAsync_PersistsTypedFdaMetadata()
    {
        var applicationId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "IND-001",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "amendment", "Amendment", DateTime.UtcNow)],
            Path.Combine(Path.GetTempPath(), "IND-001"),
            "us-fda-ectd-3.2.2");
        var repository = new StubApplicationRepository(application);
        var service = new SequencePublishingMetadataService(repository, new FdaEctd322StandardsProfileProvider());

        var updated = await service.UpdateAsync(
            applicationId,
            "0001",
            new UpdateSequencePublishingMetadataRequest(
                "IND",
                "protocol-amendment",
                "safety",
                "Updated sequence description",
                "Updated Applicant",
                "form-1571"));

        Assert.NotNull(updated);
        Assert.Equal("IND", updated!.ApplicationType);
        Assert.Equal("protocol-amendment", updated.SubmissionType);
        Assert.Equal("safety", updated.SubmissionSubtype);
        Assert.Equal("Updated sequence description", updated.SequenceDescription);
        Assert.Equal("Updated Applicant", updated.ApplicantName);
        Assert.Equal("form-1571", updated.FormType);
        Assert.Equal(1, repository.UpdateCount);

        var reloaded = await service.GetAsync(applicationId, "0001");
        Assert.Equal("protocol-amendment", reloaded!.SubmissionType);
        Assert.Equal("Updated Applicant", reloaded.ApplicantName);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenSequenceDoesNotExist()
    {
        var applicationId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "IND-001",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [],
            Path.Combine(Path.GetTempPath(), "IND-001"),
            "us-fda-ectd-3.2.2");
        var service = new SequencePublishingMetadataService(
            new StubApplicationRepository(application),
            new FdaEctd322StandardsProfileProvider());

        var metadata = await service.GetAsync(applicationId, "0000");

        Assert.Null(metadata);
    }

    private sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
    {
        private SubmissionApplication _application = application;

        public int UpdateCount { get; private set; }

        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
        {
            _application = application;
            UpdateCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == _application.Id ? _application : null);

        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([_application]);
    }
}
