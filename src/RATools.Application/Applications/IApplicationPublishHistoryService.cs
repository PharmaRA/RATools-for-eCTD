using RATools.Application.Applications.Dtos;

namespace RATools.Application.Applications;

public interface IApplicationPublishHistoryService
{
    Task<ApplicationPublishHistoryDto?> GetAsync(
        Guid applicationId,
        ApplicationPublishHistoryQuery query,
        CancellationToken cancellationToken = default);
}
