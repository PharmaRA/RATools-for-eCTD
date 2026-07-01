using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishArtifactResolver(IPublishArtifactStore artifactStore)
{
    public async Task<PublishArtifactsDto> BuildArtifactsAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outputPath = job.OutputPath;
        var reportPath = outputPath is null
            ? null
            : PublishOutputNaming.BuildPublishReportPath(outputPath, job.SequenceNumber, job.Id);

        var artifacts = new List<PublishArtifactDto>
        {
            await BuildArtifactAsync("BackboneXml", "file", outputPath, cancellationToken),
            await BuildArtifactAsync("PublishReport", "file", reportPath, cancellationToken),
            await BuildArtifactAsync("PackageZip", "file", job.PackagePath, cancellationToken)
        };

        return new PublishArtifactsDto(job.Id, job.ApplicationId, job.SequenceNumber, artifacts);
    }

    public async Task<PublishArtifactDto?> ResolveAsync(
        PublishJob job,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outputPath = job.OutputPath;
        var reportPath = outputPath is null
            ? null
            : PublishOutputNaming.BuildPublishReportPath(outputPath, job.SequenceNumber, job.Id);

        if (string.Equals(artifactName, "BackboneXml", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildArtifactAsync("BackboneXml", "file", outputPath, cancellationToken);
        }

        if (string.Equals(artifactName, "PublishReport", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildArtifactAsync("PublishReport", "file", reportPath, cancellationToken);
        }

        if (string.Equals(artifactName, "PackageZip", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildArtifactAsync("PackageZip", "file", job.PackagePath, cancellationToken);
        }

        return null;
    }

    public async Task<PublishArtifactSummaryDto?> BuildArtifactSummaryAsync(
        PublishJobDto publishJob,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(publishJob.OutputPath)
            || !await artifactStore.ExistsAsync(publishJob.OutputPath, cancellationToken))
        {
            return null;
        }

        var outputDirectory = Path.GetDirectoryName(publishJob.OutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory)
            || !await artifactStore.ExistsAsync(outputDirectory, cancellationToken))
        {
            return null;
        }

        var stats = await artifactStore.GetDirectoryStatsAsync(outputDirectory, cancellationToken);

        long packageSize = 0;
        if (!string.IsNullOrWhiteSpace(publishJob.PackagePath)
            && await artifactStore.ExistsAsync(publishJob.PackagePath, cancellationToken))
        {
            packageSize = await artifactStore.GetSizeAsync(publishJob.PackagePath, cancellationToken);
        }

        return new PublishArtifactSummaryDto(stats.FileCount, stats.TotalSizeBytes, packageSize);
    }

    private async Task<PublishArtifactDto> BuildArtifactAsync(
        string name,
        string type,
        string? path,
        CancellationToken cancellationToken)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && await artifactStore.ExistsAsync(path, cancellationToken);
        var sizeBytes = exists ? await artifactStore.GetSizeAsync(path!, cancellationToken) : 0;
        return new PublishArtifactDto(name, type, path, exists, sizeBytes, GetContentType(name));
    }

    private static string GetContentType(string artifactName)
    {
        if (string.Equals(artifactName, "BackboneXml", StringComparison.OrdinalIgnoreCase))
        {
            return "application/xml";
        }

        if (string.Equals(artifactName, "PublishReport", StringComparison.OrdinalIgnoreCase))
        {
            return "application/json";
        }

        if (string.Equals(artifactName, "PackageZip", StringComparison.OrdinalIgnoreCase))
        {
            return "application/zip";
        }

        return "application/octet-stream";
    }
}
