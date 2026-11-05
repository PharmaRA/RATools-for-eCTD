using Microsoft.AspNetCore.Mvc;
using RATools.Api.Controllers;
using RATools.Application.Finalization;
using RATools.Domain.Applications;
using RATools.Domain.Finalization;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Standards;

public sealed class FinalizationSnapshotErrorTests
{
    [Fact]
    public async Task InvalidSnapshotRefusesFinalizationWithConflictAndNoRecordedRelease()
    {
        var applications = new InMemoryApplicationRepository();
        var application = new SubmissionApplication("snapshot-error", "US", "Synthetic",
            Path.GetFullPath(".artifacts/p602-api-errors/" + Guid.NewGuid().ToString("N")), "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Snapshot integrity rejection");
        await applications.AddAsync(application);
        var store = new InMemoryFinalizationStore();
        var service = new FinalizationService(store, new InvalidCapture(), new InMemoryWorkspaceRevisionStore(applications));
        var controller = new FinalizationsController(service, store, null!);
        var response = Assert.IsType<ConflictObjectResult>(await controller.Finalize(application.Id, "0001",
            new(0, Guid.NewGuid(), "profile", "3.2.2", "3.3", new string('a', 64), "snapshot-error"), CancellationToken.None));
        Assert.Equal(409, response.StatusCode);
        Assert.Contains("FINALIZATION_EVIDENCE_INVALID", System.Text.Json.JsonSerializer.Serialize(response.Value), StringComparison.Ordinal);
        Assert.Empty(await store.ListBySequenceAsync(application.Id, "0001"));
    }

    private sealed class InvalidCapture : IFinalizationCaptureProvider
    {
        public Task<IFinalizationCapture> CaptureAsync(FinalizeSequenceRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("The archived standards resources do not match their declared delivery paths.");
        public Task<ReleaseManifest> ReadManifestAsync(FinalizedSequence snapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReleaseFileContent> OpenFileAsync(FinalizedSequence snapshot, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
