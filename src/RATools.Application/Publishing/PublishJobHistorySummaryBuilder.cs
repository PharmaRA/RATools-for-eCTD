using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Publishing;

internal static class PublishJobHistorySummaryBuilder
{
    public static PublishJobHistorySummary Create(
        PublishExecutionReportDto report,
        bool reportAvailable,
        bool reportReadable,
        string? reportError = null)
    {
        var lifecycleMatchedCount = 0;
        var lifecycleReplaceTargetNotFoundCount = 0;
        var lifecycleDeleteTargetNotFoundCount = 0;
        var lifecycleAppendTargetNotFoundCount = 0;
        var lifecycleAmbiguousCount = 0;
        var lifecycleCurrentSequenceCount = 0;

        foreach (var match in report.ValidationReport.LifecycleMatches)
        {
            switch (match.ResultCode)
            {
                case "MATCHED":
                    lifecycleMatchedCount++;
                    break;
                case "REPLACE_TARGET_NOT_FOUND":
                    lifecycleReplaceTargetNotFoundCount++;
                    break;
                case "DELETE_TARGET_NOT_FOUND":
                    lifecycleDeleteTargetNotFoundCount++;
                    break;
                case "APPEND_TARGET_NOT_FOUND":
                    lifecycleAppendTargetNotFoundCount++;
                    break;
                case "LIFECYCLE_TARGET_AMBIGUOUS":
                    lifecycleAmbiguousCount++;
                    break;
                case "LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE":
                    lifecycleCurrentSequenceCount++;
                    break;
            }
        }

        return new PublishJobHistorySummary(
            reportAvailable,
            reportReadable,
            reportError,
            report.ValidationProfile,
            report.ErrorCount,
            report.WarningCount,
            report.WarningSummary,
            report.PublishReadiness?.IsReady,
            report.PublishReadiness?.Status,
            report.PublishReadiness?.BlockingErrorCount,
            report.PublishReadiness?.WarningCount,
            report.PublishReadiness?.MissingMetadataFields?.ToArray() ?? [],
            lifecycleMatchedCount,
            lifecycleReplaceTargetNotFoundCount,
            lifecycleDeleteTargetNotFoundCount,
            lifecycleAppendTargetNotFoundCount,
            lifecycleAmbiguousCount,
            lifecycleCurrentSequenceCount,
            report.ArtifactSummary?.FileCount,
            report.ArtifactSummary?.TotalSizeBytes,
            report.ArtifactSummary?.PackageSizeBytes,
            report.ReportPath);
    }
}
