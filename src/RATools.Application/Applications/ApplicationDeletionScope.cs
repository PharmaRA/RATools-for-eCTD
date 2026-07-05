using RATools.Domain.Documents;
using RATools.Domain.Publishing;

namespace RATools.Application.Applications;

internal sealed class ApplicationDeletionScope
{
    private ApplicationDeletionScope(
        IReadOnlyCollection<DocumentPlacement> inScopePlacements,
        IReadOnlyCollection<DocumentPlacement> survivingPlacements,
        IReadOnlyCollection<PublishJob> inScopePublishJobs)
    {
        InScopePlacements = inScopePlacements;
        SurvivingPlacements = survivingPlacements;
        InScopePublishJobs = inScopePublishJobs;
        InScopeDocumentIds = inScopePlacements.Select(x => x.DocumentId).Distinct().ToHashSet();
        SurvivingDocumentIds = survivingPlacements.Select(x => x.DocumentId).Distinct().ToHashSet();
        OrphanedDocumentIds = InScopeDocumentIds.Where(x => !SurvivingDocumentIds.Contains(x)).ToArray();
    }

    public IReadOnlyCollection<DocumentPlacement> InScopePlacements { get; }

    public IReadOnlyCollection<DocumentPlacement> SurvivingPlacements { get; }

    public IReadOnlyCollection<PublishJob> InScopePublishJobs { get; }

    public IReadOnlySet<Guid> InScopeDocumentIds { get; }

    public IReadOnlySet<Guid> SurvivingDocumentIds { get; }

    public IReadOnlyCollection<Guid> OrphanedDocumentIds { get; }

    public static ApplicationDeletionScope ForApplication(
        Guid applicationId,
        IReadOnlyCollection<DocumentPlacement> allPlacements,
        IReadOnlyCollection<PublishJob> allPublishJobs)
    {
        var inScopePlacements = allPlacements.Where(x => x.ApplicationId == applicationId).ToArray();
        var survivingPlacements = allPlacements.Where(x => x.ApplicationId != applicationId).ToArray();
        var inScopePublishJobs = allPublishJobs.Where(x => x.ApplicationId == applicationId).ToArray();

        return new ApplicationDeletionScope(inScopePlacements, survivingPlacements, inScopePublishJobs);
    }

    public static ApplicationDeletionScope ForSequence(
        Guid applicationId,
        string sequenceNumber,
        IReadOnlyCollection<DocumentPlacement> allPlacements,
        IReadOnlyCollection<PublishJob> allPublishJobs)
    {
        var inScopePlacements = allPlacements
            .Where(x => x.ApplicationId == applicationId && string.Equals(x.SequenceNumber, sequenceNumber, StringComparison.Ordinal))
            .ToArray();
        var survivingPlacements = allPlacements
            .Where(x => x.ApplicationId != applicationId || !string.Equals(x.SequenceNumber, sequenceNumber, StringComparison.Ordinal))
            .ToArray();
        var inScopePublishJobs = allPublishJobs
            .Where(x => x.ApplicationId == applicationId && string.Equals(x.SequenceNumber, sequenceNumber, StringComparison.Ordinal))
            .ToArray();

        return new ApplicationDeletionScope(inScopePlacements, survivingPlacements, inScopePublishJobs);
    }

    public IReadOnlyCollection<string> CollectPurgePaths(IReadOnlyCollection<SubmissionDocument> allDocuments)
    {
        return allDocuments
            .Where(x => InScopeDocumentIds.Contains(x.Id))
            .Select(x => x.StoragePath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Concat(InScopePublishJobs.SelectMany(x => new[] { x.OutputPath, x.PackagePath })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>())
            .ToArray();
    }
}
