using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;

namespace RATools.Application.Publishing;

public interface IPublishJobService
{
    Task<PublishExecutionReportDto> ExecuteAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default);

    Task<PublishJobDto> EnqueueExecutionAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default);

    Task<PublishExecutionReportDto> ExecuteQueuedAsync(Guid jobId, CreatePublishJobRequest request, CancellationToken cancellationToken = default);

    Task<PublishExecutionReportDto?> GetExecutionReportAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PublishArtifactsDto?> GetArtifactsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PublishArtifactDownloadDto?> GetArtifactDownloadAsync(Guid id, string artifactName, CancellationToken cancellationToken = default);

    Task<PublishJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PublishJobDto>> ListAsync(CancellationToken cancellationToken = default);
}
