using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public static class DocumentStorageBoundaryExtensions
{
    public static string EnsureDocumentOwnedByPlacement(
        this IDocumentStorageBoundary boundary,
        SubmissionDocument document,
        SubmissionApplication application,
        DocumentPlacement placement,
        IReadOnlyDictionary<Guid, DocumentPlacement> placements)
    {
        var ownerSequence = placement.SequenceNumber;
        // Imported deletes have no current file: their document is the exact
        // historical target, within the same application's earlier sequence.
        if (placement.Operation == DocumentPlacementOperation.Delete
            && placement.LifecycleTargetPlacementId is { } targetId
            && placements.TryGetValue(targetId, out var target)
            && target.ApplicationId == application.Id
            && target.DocumentId == document.Id
            && target.Operation != DocumentPlacementOperation.Delete
            && string.Equals(target.CtdSection, placement.CtdSection, StringComparison.OrdinalIgnoreCase)
            && string.CompareOrdinal(target.SequenceNumber, placement.SequenceNumber) < 0)
        {
            ownerSequence = target.SequenceNumber;
        }

        return boundary.EnsureDocumentOwnedBySequence(document, application, ownerSequence);
    }
}
