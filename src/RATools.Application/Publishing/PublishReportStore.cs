using System.Text.Json;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishReportStore(IPublishArtifactStore artifactStore)
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public async Task<PublishExecutionReportDto> ReadAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Status != PublishJobStatus.Completed)
        {
            throw new PublishJobNotReadyException($"Publish job {job.Id} is in status '{job.Status}' and does not have a final report yet.");
        }

        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            throw new PublishJobReportUnavailableException($"Publish job {job.Id} completed without an output path.");
        }

        var outputDirectory = Path.GetDirectoryName(job.OutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !await artifactStore.ExistsAsync(outputDirectory, cancellationToken))
        {
            throw new PublishJobReportUnavailableException($"Publish output directory for job {job.Id} no longer exists.");
        }

        var expectedReportPath = PublishOutputNaming.BuildPublishReportPath(job.OutputPath, job.SequenceNumber, job.Id);
        if (!await artifactStore.ExistsAsync(expectedReportPath, cancellationToken))
        {
            throw new PublishJobReportUnavailableException($"Publish report for job {job.Id} was not found at '{expectedReportPath}'.");
        }

        try
        {
            var json = await artifactStore.ReadAllTextAsync(expectedReportPath, cancellationToken);
            var report = JsonSerializer.Deserialize<PublishExecutionReportDto>(json, ReadOptions);

            return report ?? throw new PublishJobReportCorruptedException($"Publish report for job {job.Id} could not be deserialized.");
        }
        catch (JsonException exception)
        {
            throw new PublishJobReportCorruptedException($"Publish report for job {job.Id} is corrupted: {exception.Message}");
        }
    }

    public async Task WriteAsync(PublishExecutionReportDto report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(report.ReportPath))
        {
            return;
        }

        var json = JsonSerializer.Serialize(report, WriteOptions);
        await artifactStore.WriteAllTextAsync(report.ReportPath, json, cancellationToken);
    }
}
