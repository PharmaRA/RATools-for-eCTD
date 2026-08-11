using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Applications;

public sealed class ApplicationPublishHistoryService(
    IApplicationRepository applicationRepository,
    IPublishJobRepository publishJobRepository) : IApplicationPublishHistoryService
{
    public async Task<ApplicationPublishHistoryDto?> GetAsync(
        Guid applicationId,
        ApplicationPublishHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var result = await publishJobRepository.QueryHistoryAsync(
            new PublishJobHistoryQuery(
                applicationId,
                query.SequenceNumber,
                query.Status,
                query.CreatedFromUtc,
                query.CreatedToUtc,
                query.Page,
                query.PageSize,
                query.ReadinessStatus),
            cancellationToken);

        var entries = result.Items
            .Select(job =>
            {
                PublishJobHistorySummary? summary = null;
                result.HistorySummaries?.TryGetValue(job.Id, out summary);
                return new ApplicationPublishHistoryEntryDto(
                    job.Id,
                    job.SequenceNumber,
                    job.Status.ToString(),
                    job.CreatedUtc,
                    job.CompletedUtc,
                    summary?.ReportAvailable ?? false,
                    summary?.ReportReadable ?? false,
                    summary?.ReportError,
                    summary?.ValidationProfile,
                    summary?.ErrorCount,
                    summary?.WarningCount,
                    summary?.WarningSummary,
                    BuildReadinessSummary(summary),
                    BuildLifecycleSummary(summary),
                    [],
                    BuildArtifactSummary(summary),
                    summary?.ReportPath,
                    job.PackagePath);
            })
            .ToArray();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var readinessCounts = result.ReadinessCounts
            ?? new PublishJobHistoryReadinessCounts(0, 0, result.TotalCount);
        var lifecycleCounts = result.LifecycleCounts
            ?? new PublishJobHistoryLifecycleCounts(0, 0, 0, 0, 0, 0);

        return new ApplicationPublishHistoryDto(
            application.Id,
            application.ApplicationNumber,
            application.SponsorName,
            page,
            pageSize,
            result.TotalCount,
            new ApplicationPublishHistoryStatusSummaryDto(
                result.CompletedCount,
                result.FailedCount,
                result.RunningCount),
            new ApplicationPublishHistoryReadinessAggregateDto(
                readinessCounts.Ready,
                readinessCounts.Blocked,
                readinessCounts.Unknown),
            new ApplicationPublishHistoryLifecycleSummaryDto(
                lifecycleCounts.Matched,
                lifecycleCounts.ReplaceTargetNotFound,
                lifecycleCounts.DeleteTargetNotFound,
                lifecycleCounts.AppendTargetNotFound,
                lifecycleCounts.Ambiguous,
                lifecycleCounts.CurrentSequence),
            entries);
    }

    private static ApplicationPublishHistoryLifecycleSummaryDto BuildLifecycleSummary(PublishJobHistorySummary? summary)
    {
        return summary is null
            ? new ApplicationPublishHistoryLifecycleSummaryDto(0, 0, 0, 0, 0, 0)
            : new ApplicationPublishHistoryLifecycleSummaryDto(
                summary.LifecycleMatchedCount,
                summary.LifecycleReplaceTargetNotFoundCount,
                summary.LifecycleDeleteTargetNotFoundCount,
                summary.LifecycleAppendTargetNotFoundCount,
                summary.LifecycleAmbiguousCount,
                summary.LifecycleCurrentSequenceCount);
    }

    private static ApplicationPublishHistoryReadinessSummaryDto? BuildReadinessSummary(PublishJobHistorySummary? summary)
    {
        if (summary?.ReadinessStatus is null || !summary.ReadinessIsReady.HasValue)
        {
            return null;
        }

        return new ApplicationPublishHistoryReadinessSummaryDto(
            summary.ReadinessIsReady.Value,
            summary.ReadinessStatus,
            summary.ReadinessBlockingErrorCount ?? 0,
            summary.ReadinessWarningCount ?? 0,
            summary.ReadinessMissingMetadataFields);
    }

    private static PublishArtifactSummaryDto? BuildArtifactSummary(PublishJobHistorySummary? summary)
    {
        if (summary?.ArtifactFileCount is null
            || summary.ArtifactTotalSizeBytes is null
            || summary.ArtifactPackageSizeBytes is null)
        {
            return null;
        }

        return new PublishArtifactSummaryDto(
            summary.ArtifactFileCount.Value,
            summary.ArtifactTotalSizeBytes.Value,
            summary.ArtifactPackageSizeBytes.Value);
    }
}
