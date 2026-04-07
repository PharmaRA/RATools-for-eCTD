using System.Text.Json;
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

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;

        var entries = new List<ApplicationPublishHistoryEntryDto>();

        var result = await publishJobRepository.QueryHistoryAsync(
            new PublishJobHistoryQuery(
                applicationId,
                query.SequenceNumber,
                query.Status,
                query.CreatedFromUtc,
                query.CreatedToUtc,
                page,
                pageSize),
            cancellationToken);

        var filteredJobs = result.Items;
        var totalCount = result.TotalCount;
        var statusSummary = new ApplicationPublishHistoryStatusSummaryDto(
            result.CompletedCount,
            result.FailedCount,
            result.RunningCount);

        foreach (var job in filteredJobs)
        {
            var reportState = await TryReadReportAsync(job, cancellationToken);
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
                reportState.Report?.ArtifactSummary,
                reportState.Report?.ReportPath,
                job.PackagePath));
        }

        return new ApplicationPublishHistoryDto(
            application.Id,
            application.ApplicationNumber,
            application.Region,
            application.SponsorName,
            page,
            pageSize,
            totalCount,
            statusSummary,
            entries);
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

        var reportPath = Path.Combine(outputDirectory, $"publish-report-{job.SequenceNumber}-{job.Id:N}.json");
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
}
