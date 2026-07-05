using RATools.Application.Applications.Dtos;
using RATools.Application.Validation.Dtos;

namespace RATools.Application.Applications;

internal static class ApplicationPublishHistoryLifecycleSummary
{
    public static ApplicationPublishHistoryLifecycleSummaryDto Create(IEnumerable<ValidationLifecycleMatchDto> lifecycleMatches)
    {
        var matchedCount = 0;
        var replaceTargetNotFoundCount = 0;
        var deleteTargetNotFoundCount = 0;
        var appendTargetNotFoundCount = 0;
        var ambiguousCount = 0;
        var currentSequenceCount = 0;

        foreach (var match in lifecycleMatches)
        {
            switch (match.ResultCode)
            {
                case "MATCHED":
                    matchedCount++;
                    break;
                case "REPLACE_TARGET_NOT_FOUND":
                    replaceTargetNotFoundCount++;
                    break;
                case "DELETE_TARGET_NOT_FOUND":
                    deleteTargetNotFoundCount++;
                    break;
                case "APPEND_TARGET_NOT_FOUND":
                    appendTargetNotFoundCount++;
                    break;
                case "LIFECYCLE_TARGET_AMBIGUOUS":
                    ambiguousCount++;
                    break;
                case "LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE":
                    currentSequenceCount++;
                    break;
            }
        }

        return new ApplicationPublishHistoryLifecycleSummaryDto(
            matchedCount,
            replaceTargetNotFoundCount,
            deleteTargetNotFoundCount,
            appendTargetNotFoundCount,
            ambiguousCount,
            currentSequenceCount);
    }
}
