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
        var effectiveTitle = GetEffectiveTitle(placement, currentDocument);

        var currentMatches = currentSequencePlacements
            .Where(x => x.Id != placement.Id)
            .Where(x => x.CtdSection == placement.CtdSection)
            .Where(x => x.DocumentId == placement.DocumentId ||
                        (documentById.TryGetValue(x.DocumentId, out var currentMatchDocument) &&
                         GetEffectiveTitle(x, currentMatchDocument) == effectiveTitle))
            .ToArray();

        if (currentMatches.Length > 0)
        {
            return new LifecycleTargetResolution(
                "LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE",
                "CurrentSequence",
                ["CurrentSequence"],
                currentMatches.Length,
                currentMatches.Select(x => x.SequenceNumber).Distinct().OrderBy(x => x).ToArray(),
                currentMatches.Select(x => x.Id).ToArray(),
                "CurrentSequence");
        }

        var attemptedStrategies = new List<string> { "DocumentId" };

        var documentIdMatches = historicalPlacements
            .Where(x => x.DocumentId == placement.DocumentId)
            .ToArray();

        if (documentIdMatches.Length > 1)
        {
            return CreateAmbiguousResult(documentIdMatches, "DocumentId", attemptedStrategies);
        }

        if (documentIdMatches.Length == 1)
        {
            return CreateMatchedResult(documentIdMatches, "DocumentId", attemptedStrategies);
        }

        if (historicalPlacements.Count > 0)
        {
            attemptedStrategies.Add("EffectiveTitle");

            var effectiveTitleMatches = historicalPlacements
                .Where(x => documentById.TryGetValue(x.DocumentId, out var historicalDocument) &&
                            GetEffectiveTitle(x, historicalDocument) == effectiveTitle)
                .ToArray();

            if (effectiveTitleMatches.Length > 1)
            {
                return CreateAmbiguousResult(effectiveTitleMatches, "EffectiveTitle", attemptedStrategies);
            }

            if (effectiveTitleMatches.Length == 1)
            {
                return CreateMatchedResult(effectiveTitleMatches, "EffectiveTitle", attemptedStrategies);
            }
        }

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

    private static LifecycleTargetResolution CreateMatchedResult(
        IReadOnlyCollection<DocumentPlacement> matches,
        string strategy,
        IReadOnlyCollection<string> attemptedStrategies)
    {
        return new LifecycleTargetResolution(
            "MATCHED",
            strategy,
            attemptedStrategies,
            matches.Count,
            matches.Select(x => x.SequenceNumber).Distinct().OrderBy(x => x).ToArray(),
            matches.Select(x => x.Id).ToArray(),
            "Active");
    }

    private static LifecycleTargetResolution CreateAmbiguousResult(
        IReadOnlyCollection<DocumentPlacement> matches,
        string strategy,
        IReadOnlyCollection<string> attemptedStrategies)
    {
        return new LifecycleTargetResolution(
            "LIFECYCLE_TARGET_AMBIGUOUS",
            strategy,
            attemptedStrategies,
            matches.Count,
            matches.Select(x => x.SequenceNumber).Distinct().OrderBy(x => x).ToArray(),
            matches.Select(x => x.Id).ToArray(),
            "Active");
    }

    private static string GetEffectiveTitle(DocumentPlacement placement, SubmissionDocument document)
    {
        return string.IsNullOrWhiteSpace(placement.Title) ? document.FileName : placement.Title.Trim();
    }
}
