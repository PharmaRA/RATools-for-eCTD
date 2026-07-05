using RATools.Application.Applications;
using RATools.Domain.Documents;
using RATools.Domain.Publishing;

namespace RATools.Tests.Applications;

public sealed class ApplicationDeletionScopeTests
{
    [Fact]
    public void ForSequence_ComputesScopedResourcesAndPurgePaths()
    {
        var applicationId = Guid.NewGuid();
        var currentDocumentId = Guid.NewGuid();
        var historicalDocumentId = Guid.NewGuid();
        var currentPlacement = Placement(applicationId, "0002", currentDocumentId);
        var historicalPlacement = Placement(applicationId, "0001", historicalDocumentId);
        var currentJob = Job(applicationId, "0002", @"C:\out\0002\index.xml", @"C:\out\0002.zip");
        var historicalJob = Job(applicationId, "0001", @"C:\out\0001\index.xml", @"C:\out\0001.zip");
        var currentDocument = Document(currentDocumentId, @"C:\work\app\0002\m1\file.pdf");
        var historicalDocument = Document(historicalDocumentId, @"C:\work\app\0001\m1\file.pdf");

        var scope = ApplicationDeletionScope.ForSequence(
            applicationId,
            "0002",
            [currentPlacement, historicalPlacement],
            [currentJob, historicalJob]);

        Assert.Equal([currentPlacement.Id], scope.InScopePlacements.Select(x => x.Id));
        Assert.Equal([currentJob.Id], scope.InScopePublishJobs.Select(x => x.Id));
        Assert.Equal([currentDocumentId], scope.OrphanedDocumentIds);
        Assert.Equal(
            [currentDocument.StoragePath, currentJob.OutputPath!, currentJob.PackagePath!],
            scope.CollectPurgePaths([currentDocument, historicalDocument]));
    }

    [Fact]
    public void ForApplication_DoesNotOrphanDocumentStillReferencedOutsideScope()
    {
        var applicationId = Guid.NewGuid();
        var otherApplicationId = Guid.NewGuid();
        var sharedDocumentId = Guid.NewGuid();
        var applicationOnlyDocumentId = Guid.NewGuid();

        var scope = ApplicationDeletionScope.ForApplication(
            applicationId,
            [
                Placement(applicationId, "0001", sharedDocumentId),
                Placement(applicationId, "0001", applicationOnlyDocumentId),
                Placement(otherApplicationId, "0001", sharedDocumentId)
            ],
            []);

        Assert.Equal([applicationOnlyDocumentId], scope.OrphanedDocumentIds);
        Assert.Contains(sharedDocumentId, scope.SurvivingDocumentIds);
    }

    private static DocumentPlacement Placement(Guid applicationId, string sequenceNumber, Guid documentId)
        => DocumentPlacement.Rehydrate(
            Guid.NewGuid(),
            documentId,
            applicationId,
            sequenceNumber,
            "m1.1",
            DocumentPlacementOperation.New,
            null,
            null,
            DateTime.UtcNow);

    private static PublishJob Job(Guid applicationId, string sequenceNumber, string outputPath, string packagePath)
        => PublishJob.Rehydrate(
            Guid.NewGuid(),
            applicationId,
            sequenceNumber,
            PublishJobStatus.Completed,
            outputPath,
            packagePath,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

    private static SubmissionDocument Document(Guid id, string storagePath)
        => SubmissionDocument.Rehydrate(
            id,
            Path.GetFileName(storagePath),
            "application/pdf",
            1,
            "sha256",
            "md5",
            storagePath,
            DateTime.UtcNow);
}
