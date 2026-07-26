using RATools.Domain.Documents;

namespace RATools.Application.Validation;

/// <summary>
/// 生命周期目标解析：两级策略 + 历史链回放。
/// 策略 1（ExplicitPlacementId）：用户显式指定目标 placement。
/// 策略 2（SectionAndFileName）：未显式指定时，在同 section 的历史 placement 中按
/// 文件名唯一匹配自动解析；多解 → LIFECYCLE_TARGET_AMBIGUOUS，零解 → *_TARGET_NOT_FOUND。
/// 链回放：目标一旦被更晚历史序列的 replace 超越或 delete 删除即非 Active，
/// 发 LIFECYCLE_TARGET_SUPERSEDED / LIFECYCLE_TARGET_DELETED——
/// replace 一个已删除的 leaf 是官方验证器必拒的提交错误，此前完全检不出来。
/// </summary>
public static class LifecycleTargetResolver
{
    private static readonly string[] AllStrategies = ["ExplicitPlacementId", "SectionAndFileName"];

    public static LifecycleTargetResolution Resolve(
        DocumentPlacement placement,
        SubmissionDocument currentDocument,
        IReadOnlyCollection<DocumentPlacement> currentSequencePlacements,
        IReadOnlyCollection<DocumentPlacement> historicalPlacements,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        _ = currentSequencePlacements;

        if (placement.LifecycleTargetPlacementId is { } explicitTargetId)
        {
            var explicitTarget = historicalPlacements.SingleOrDefault(x => x.Id == explicitTargetId);
            if (explicitTarget is null || !documentById.ContainsKey(explicitTarget.DocumentId))
            {
                return new LifecycleTargetResolution(
                    "LIFECYCLE_TARGET_INVALID",
                    "ExplicitPlacementId",
                    ["ExplicitPlacementId"],
                    0,
                    Array.Empty<string>(),
                    [explicitTargetId],
                    "Invalid");
            }

            return ClassifyTargetState(explicitTarget, "ExplicitPlacementId", ["ExplicitPlacementId"], historicalPlacements);
        }

        // 策略 2：同 section + 同文件名 的历史 placement 唯一匹配。
        // historicalPlacements 已由调用方限定为同 section 且早于当前序列。
        var fileNameCandidates = historicalPlacements
            .Where(candidate => documentById.TryGetValue(candidate.DocumentId, out var candidateDocument)
                && string.Equals(candidateDocument.FileName, currentDocument.FileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.SequenceNumber, StringComparer.Ordinal)
            .ToArray();

        if (fileNameCandidates.Length == 0)
        {
            return CreateNotFoundResult(placement, AllStrategies);
        }

        if (fileNameCandidates.Length > 1)
        {
            return new LifecycleTargetResolution(
                "LIFECYCLE_TARGET_AMBIGUOUS",
                "SectionAndFileName",
                AllStrategies,
                fileNameCandidates.Length,
                fileNameCandidates.Select(x => x.SequenceNumber).Distinct().ToArray(),
                fileNameCandidates.Select(x => x.Id).ToArray(),
                "Ambiguous");
        }

        return ClassifyTargetState(fileNameCandidates[0], "SectionAndFileName", AllStrategies, historicalPlacements);
    }

    /// <summary>
    /// 历史链回放：目标 placement 若已被更晚历史序列中的 replace 指向（Superseded）
    /// 或被 delete 指向（Deleted），则不再是 Active，不能作为生命周期操作目标。
    /// </summary>
    private static LifecycleTargetResolution ClassifyTargetState(
        DocumentPlacement target,
        string matchStrategy,
        IReadOnlyCollection<string> attemptedStrategies,
        IReadOnlyCollection<DocumentPlacement> historicalPlacements)
    {
        var successor = historicalPlacements
            .Where(candidate => candidate.LifecycleTargetPlacementId == target.Id)
            .Where(candidate => string.Compare(candidate.SequenceNumber, target.SequenceNumber, StringComparison.Ordinal) > 0)
            .OrderByDescending(candidate => candidate.SequenceNumber, StringComparer.Ordinal)
            .FirstOrDefault();

        if (successor is not null && successor.Operation == DocumentPlacementOperation.Delete)
        {
            return new LifecycleTargetResolution(
                "LIFECYCLE_TARGET_DELETED",
                matchStrategy,
                attemptedStrategies,
                1,
                [target.SequenceNumber, successor.SequenceNumber],
                [target.Id, successor.Id],
                "Deleted");
        }

        if (successor is not null && successor.Operation == DocumentPlacementOperation.Replace)
        {
            return new LifecycleTargetResolution(
                "LIFECYCLE_TARGET_SUPERSEDED",
                matchStrategy,
                attemptedStrategies,
                1,
                [target.SequenceNumber, successor.SequenceNumber],
                [target.Id, successor.Id],
                "Superseded");
        }

        return new LifecycleTargetResolution(
            "MATCHED",
            matchStrategy,
            attemptedStrategies,
            1,
            [target.SequenceNumber],
            [target.Id],
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
