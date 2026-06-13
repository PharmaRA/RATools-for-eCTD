using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Validation.Dtos;

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
                1,
                int.MaxValue),
            cancellationToken);

        var entries = new List<ApplicationPublishHistoryEntryDto>();
        var lifecycleMatches = new List<ValidationLifecycleMatchDto>();

        foreach (var job in result.Items)
        {
            var reportState = await TryReadReportAsync(job, cancellationToken);
            var entryLifecycleMatches = reportState.Report?.ValidationReport?.LifecycleMatches?.ToArray() ?? Array.Empty<ValidationLifecycleMatchDto>();
            var readinessSummary = BuildReadinessSummary(reportState.Report?.PublishReadiness);

            if (!MatchesReadinessFilter(readinessSummary, query.ReadinessStatus))
            {
                continue;
            }

            if (entryLifecycleMatches.Length > 0)
            {
                lifecycleMatches.AddRange(entryLifecycleMatches);
            }

            entries.Add(new ApplicationPublishHistoryEntryDto(
                job.Id,
                job.SequenceNumber,
                job.Status.ToString(),
                job.CreatedUtc,
                job.CompletedUtc,
                reportState.Exists,
                reportState.Readable,
                reportState.Error,
                reportState.Report?.ValidationProfile,
                reportState.Report?.ErrorCount,
                reportState.Report?.WarningCount,
                reportState.Report?.WarningSummary,
                readinessSummary,
                BuildLifecycleSummary(entryLifecycleMatches),
                entryLifecycleMatches,
                reportState.Report?.ArtifactSummary,
                reportState.Report?.ReportPath,
                job.PackagePath));
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var pagedEntries = entries
            .OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();
        var filteredStatuses = entries
            .GroupBy(x => x.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var statusSummary = new ApplicationPublishHistoryStatusSummaryDto(
            filteredStatuses.GetValueOrDefault("Completed", 0),
            filteredStatuses.GetValueOrDefault("Failed", 0),
            filteredStatuses.GetValueOrDefault("Running", 0));
        var lifecycleSummary = BuildLifecycleSummary(lifecycleMatches);

        return new ApplicationPublishHistoryDto(
            application.Id,
            application.ApplicationNumber,
            application.SponsorName,
            page,
            pageSize,
            entries.Count,
            statusSummary,
            lifecycleSummary,
            pagedEntries);
    }

    private static async Task<(PublishExecutionReportDto? Report, bool Exists, bool Readable, string? Error)> TryReadReportAsync(
        Domain.Publishing.PublishJob job,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            return (null, false, false, null);
        }

        var outputDirectory = Path.GetDirectoryName(job.OutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return (null, false, false, null);
        }

        var reportPath = PublishOutputNaming.BuildPublishReportPath(job.OutputPath, job.SequenceNumber, job.Id);
        if (!File.Exists(reportPath))
        {
            return (null, false, false, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(reportPath, cancellationToken);
            var report = JsonSerializer.Deserialize<PublishExecutionReportDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return report is null
                ? (null, true, false, "Report file could not be deserialized.")
                : (report, true, true, null);
        }
        catch (Exception exception)
        {
            return (null, true, false, exception.Message);
        }
    }

    private static ApplicationPublishHistoryLifecycleSummaryDto BuildLifecycleSummary(IReadOnlyCollection<ValidationLifecycleMatchDto> lifecycleMatches)
    {
        return new ApplicationPublishHistoryLifecycleSummaryDto(
            lifecycleMatches.Count(x => x.ResultCode == "MATCHED"),
            lifecycleMatches.Count(x => x.ResultCode == "REPLACE_TARGET_NOT_FOUND"),
            lifecycleMatches.Count(x => x.ResultCode == "DELETE_TARGET_NOT_FOUND"),
            lifecycleMatches.Count(x => x.ResultCode == "APPEND_TARGET_NOT_FOUND"),
            lifecycleMatches.Count(x => x.ResultCode == "LIFECYCLE_TARGET_AMBIGUOUS"),
            lifecycleMatches.Count(x => x.ResultCode == "LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE"));
    }

    private static ApplicationPublishHistoryReadinessSummaryDto? BuildReadinessSummary(PublishReadinessReportDto? readiness)
    {
        if (readiness is null)
        {
            return null;
        }

        return new ApplicationPublishHistoryReadinessSummaryDto(
            readiness.IsReady,
            readiness.Status,
            readiness.BlockingErrorCount,
            readiness.WarningCount,
            readiness.MissingMetadataFields?.ToArray() ?? Array.Empty<string>());
    }

    private static bool MatchesReadinessFilter(
        ApplicationPublishHistoryReadinessSummaryDto? readiness,
        string? readinessStatus)
    {
        if (string.IsNullOrWhiteSpace(readinessStatus))
        {
            return true;
        }

        if (string.Equals(readinessStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return readiness is null;
        }

        return readiness is not null
            && readiness.Status.Equals(readinessStatus, StringComparison.OrdinalIgnoreCase);
    }
}
