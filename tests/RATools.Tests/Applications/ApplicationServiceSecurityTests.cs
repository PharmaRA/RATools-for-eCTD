using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;

namespace RATools.Tests.Applications;

public sealed class ApplicationServiceSecurityTests
{
    [Fact]
    public async Task CreateAsync_RejectsWorkingDirectoryOutsideConfiguredRootsBeforeCreatingDirectory()
    {
        var workspaceService = new RecordingWorkspaceService();
        var service = new ApplicationService(
            new StubApplicationRepository(),
            new StubApplicationDeletionCoordinator(),
            new RejectingWorkspacePathPolicy(),
            workspaceService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreateApplicationRequest(
            "app-001",
            "us-fda-ectd-3.2.2",
            "Sponsor",
            Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}"))));

        Assert.Contains("outside the configured workspace roots", exception.Message);
        Assert.False(workspaceService.EnsureApplicationCalled);
    }

    [Fact]
    public async Task CreateAsync_UsesAllowedNormalizedWorkingDirectoryForCreation()
    {
        var requestedParent = Path.Combine(Path.GetTempPath(), $"allowed-{Guid.NewGuid():N}");
        var applicationNumber = "app-001";
        var normalizedFinalPath = Path.Combine(requestedParent, applicationNumber);
        var workspacePathPolicy = new RecordingWorkspacePathPolicy(normalizedFinalPath);
        var workspaceService = new RecordingWorkspaceService();
        var service = new ApplicationService(
            new StubApplicationRepository(),
            new StubApplicationDeletionCoordinator(),
            workspacePathPolicy,
            workspaceService);

        await service.CreateAsync(new CreateApplicationRequest(
            applicationNumber,
            "us-fda-ectd-3.2.2",
            "Sponsor",
            requestedParent));

        Assert.Equal(Path.Combine(requestedParent, applicationNumber), workspacePathPolicy.LastRequestedPath);
        Assert.True(workspaceService.EnsureApplicationCalled);
        Assert.Equal(Path.GetDirectoryName(normalizedFinalPath), workspaceService.LastParentPath);
        Assert.Equal(Path.GetFileName(normalizedFinalPath), workspaceService.LastApplicationNumber);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateApplicationNumberCaseInsensitivelyBeforeCreatingDirectory()
    {
        var workspaceService = new RecordingWorkspaceService();
        var service = new ApplicationService(
            new StubApplicationRepository([new SubmissionApplication("APP-001", "United States", "Sponsor", Path.GetTempPath(), "us-fda-ectd-3.2.2")]),
            new StubApplicationDeletionCoordinator(),
            new RecordingWorkspacePathPolicy(Path.Combine(Path.GetTempPath(), "app-001")),
            workspaceService);

        var exception = await Assert.ThrowsAsync<ApplicationNumberAlreadyExistsException>(() => service.CreateAsync(new CreateApplicationRequest(
            "app-001",
            "us-fda-ectd-3.2.2",
            "Sponsor",
            Path.GetTempPath())));

        Assert.Contains("app-001", exception.Message);
        Assert.False(workspaceService.EnsureApplicationCalled);
    }

    private sealed class RejectingWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path)
            => throw new InvalidOperationException($"Path '{path}' is outside the configured workspace roots.");
    }

    private sealed class RecordingWorkspacePathPolicy(string allowedPath) : IWorkspacePathPolicy
    {
        public string? LastRequestedPath { get; private set; }

        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path)
        {
            LastRequestedPath = path;
            return allowedPath;
        }
    }

    private sealed class RecordingWorkspaceService : IApplicationWorkspaceService
    {
        public bool EnsureApplicationCalled { get; private set; }
        public string? LastParentPath { get; private set; }
        public string? LastApplicationNumber { get; private set; }

        public Task<string> EnsureApplicationWorkingDirectoryAsync(string parentPath, string applicationNumber, CancellationToken cancellationToken = default)
        {
            EnsureApplicationCalled = true;
            LastParentPath = parentPath;
            LastApplicationNumber = applicationNumber;
            return Task.FromResult(Path.Combine(parentPath, applicationNumber));
        }

        public Task<string> EnsureSequenceWorkingDirectoryAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.Combine(applicationWorkingDirectoryPath, sequenceNumber));
    }

    private sealed class StubApplicationRepository(IReadOnlyCollection<SubmissionApplication>? applications = null) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<SubmissionApplication?>(null);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(applications ?? []);
    }

    private sealed class StubApplicationDeletionCoordinator : IApplicationDeletionCoordinator
    {
        public Task DeleteApplicationAsync(SubmissionApplication application, ApplicationDeleteMode deleteMode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteSequenceAsync(SubmissionApplication application, string sequenceNumber, ApplicationDeleteMode deleteMode, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
