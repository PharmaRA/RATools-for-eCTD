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

        var jobs = await publishJobRepository.ListAsync(cancellationToken);
        var entries = new List<ApplicationPublishHistoryEntryDto>();

        var filteredJobs = jobs
            .Where(x => x.ApplicationId == applicationId)
            .Where(x => string.IsNullOrWhiteSpace(query.SequenceNumber) || x.SequenceNumber == query.SequenceNumber)
            .Where(x => string.IsNullOrWhiteSpace(query.Status) || x.Status.ToString().Equals(query.Status, StringComparison.OrdinalIgnoreCase))
            .Where(x => !query.CreatedFromUtc.HasValue || x.CreatedUtc >= query.CreatedFromUtc.Value)
            .Where(x => !query.CreatedToUtc.HasValue || x.CreatedUtc <= query.CreatedToUtc.Value)
            .OrderByDescending(x => x.CreatedUtc)
            .ToArray();

        var totalCount = filteredJobs.Length;
        var statusSummary = new ApplicationPublishHistoryStatusSummaryDto(
            filteredJobs.Count(x => x.Status == Domain.Publishing.PublishJobStatus.Completed),
            filteredJobs.Count(x => x.Status == Domain.Publishing.PublishJobStatus.Failed),
            filteredJobs.Count(x => x.Status == Domain.Publishing.PublishJobStatus.Running));

        foreach (var job in filteredJobs
                     .Skip((page - 1) * pageSize)
                     .Take(pageSize))
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
