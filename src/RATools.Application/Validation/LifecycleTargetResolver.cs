using RATools.Domain.Documents;

namespace RATools.Application.Validation;

public static class LifecycleTargetResolver
{
    public static LifecycleTargetResolution Resolve(
        DocumentPlacement placement,
        SubmissionDocument currentDocument,
        IReadOnlyCollection<DocumentPlacement> currentSequencePlacements,
        IReadOnlyCollection<DocumentPlacement> historicalPlacements,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        _ = currentDocument;
        _ = currentSequencePlacements;
        _ = documentById;

        if (placement.LifecycleTargetPlacementId is null)
        {
            return CreateNotFoundResult(placement, ["ExplicitPlacementId"]);
        }

        var explicitTarget = historicalPlacements.SingleOrDefault(x => x.Id == placement.LifecycleTargetPlacementId.Value);
        if (explicitTarget is null || !documentById.ContainsKey(explicitTarget.DocumentId))
        {
            return new LifecycleTargetResolution(
                "LIFECYCLE_TARGET_INVALID",
                "ExplicitPlacementId",
                ["ExplicitPlacementId"],
                0,
                Array.Empty<string>(),
                [placement.LifecycleTargetPlacementId.Value],
                "Invalid");
        }

        return new LifecycleTargetResolution(
            "MATCHED",
            "ExplicitPlacementId",
            ["ExplicitPlacementId"],
            1,
            [explicitTarget.SequenceNumber],
            [explicitTarget.Id],
            "Active");
    }

    private static LifecycleTargetResolution CreateNotFoundResult(DocumentPlacement placement, IReadOnlyCollection<string> attemptedStrategies)
    {
        var notFoundCode = placement.Operation switch
        {
            DocumentPlacementOperation.Replace => "REPLACE_TARGET_NOT_FOUND",
            DocumentPlacementOperation.Delete => "DELETE_TARGET_NOT_FOUND",
            DocumentPlacementOperation.Append => "APPEND_TARGET_NOT_FOUND",
            _ => throw new InvalidOperationException($"Unsupported lifecycle operation '{placement.Operation}'.")
        };

        return new LifecycleTargetResolution(
            notFoundCode,
            "None",
            attemptedStrategies,
            0,
            Array.Empty<string>(),
            Array.Empty<Guid>(),
            "NotFound");
    }

}
