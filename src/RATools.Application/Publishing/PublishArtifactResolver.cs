using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishArtifactResolver(IPublishArtifactStore artifactStore)
{
    public async Task<PublishArtifactsDto> BuildArtifactsAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var artifacts = new List<PublishArtifactDto>();
        foreach (var descriptor in PublishArtifactDescriptorCatalog.All)
        {
            artifacts.Add(await BuildArtifactAsync(descriptor, job, cancellationToken));
        }

        return new PublishArtifactsDto(job.Id, job.ApplicationId, job.SequenceNumber, artifacts);
    }

    public async Task<PublishArtifactDto?> ResolveAsync(
        PublishJob job,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = PublishArtifactDescriptorCatalog.Find(artifactName);
        return descriptor is null
            ? null
            : await BuildArtifactAsync(descriptor, job, cancellationToken);
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
        PublishArtifactDescriptor descriptor,
        PublishJob job,
        CancellationToken cancellationToken)
    {
        var path = descriptor.ResolvePath(job);
        var exists = !string.IsNullOrWhiteSpace(path) && await artifactStore.ExistsAsync(path, cancellationToken);
        var sizeBytes = exists ? await artifactStore.GetSizeAsync(path!, cancellationToken) : 0;
        return new PublishArtifactDto(
            descriptor.Name,
            descriptor.Type,
            path,
            exists,
            sizeBytes,
            descriptor.ContentType);
    }
}
